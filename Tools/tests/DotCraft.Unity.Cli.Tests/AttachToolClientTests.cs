using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity.Cli.Tests;

public sealed class AttachToolClientTests
{
    [Fact]
    public async Task UnknownToolAndTargetListingDoNotConnect()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root);
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        Assert.Single(client.ListTargets());
        var result = await client.CallAsync("project_custom", null, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("ToolNotFound", result.ErrorCode);
        Assert.Equal(0, attach.ConnectCount);
    }

    [Fact]
    public async Task AttachesOnlyToUniqueProjectAndPreservesResultEnvelope()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root);
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var result = await client.CallAsync(UnityTools.ExecuteCSharp, Code(), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal(42, result.Result!.Value.GetProperty("returnValue").GetInt32());
        Assert.Equal("editor", result.Result.Value.GetProperty("mode").GetString());
        Assert.Equal(1, attach.ConnectCount);
        Assert.Equal(1, attach.ExecuteCount);
    }

    [Fact]
    public async Task AttachPreservesExecutionErrorCode()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root)
        {
            Execute = _ => Task.FromResult(new JsonObject
            {
                ["state"] = "failed",
                ["errorCode"] = "UnityExecutionEntryPointInvalid",
                ["error"] = "The compiled Unity entry type was not found."
            })
        };
        await using var client = new AttachToolClient(fixture.Root, null, attach);

        var result = await client.CallAsync(
            UnityTools.ExecuteCSharp,
            Code(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("UnityExecutionEntryPointInvalid", result.ErrorCode);
    }

    [Fact]
    public async Task AmbiguousOrMismatchedTargetsDoNotConnect()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root) { Targets = [new(11, fixture.Root, "Unity.exe"), new(12, fixture.Root, "Unity.exe")] };
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var result = await client.CallAsync(UnityTools.ExecuteCSharp, Code(), TestContext.Current.CancellationToken);
        Assert.Contains("Multiple", result.ErrorMessage);
        Assert.Equal(0, attach.ConnectCount);
        await using var selected = new AttachToolClient(fixture.Root, 12, attach);
        Assert.True((await selected.CallAsync(UnityTools.ExecuteCSharp, Code(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(12, attach.LastPid);
    }

    [Fact]
    public async Task PlayModeDoesNotEnterPlayModeOrExecuteWhenEditorIsStopped()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root);
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var args = Code();
        args["mode"] = JsonSerializer.SerializeToElement("playmode");
        var result = await client.CallAsync(UnityTools.ExecuteCSharp, args, TestContext.Current.CancellationToken);
        Assert.Equal("UnityNotInPlayMode", result.ErrorCode);
        Assert.Equal(0, attach.ExecuteCount);
    }

    [Fact]
    public async Task CancellationReachesAttachExecutionAndDoesNotReplay()
    {
        using var fixture = new CliFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attach = new FakeAttach(fixture.Root)
        {
            Execute = async token => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(); }
        };
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var pending = client.CallAsync(UnityTools.ExecuteCSharp, Code(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, attach.ExecuteCount);
    }

    [Fact]
    public async Task ScriptPathIsResolvedRelativeToProjectBeforeDispatch()
    {
        using var fixture = new CliFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "read.cs"), "return 123;");
        var attach = new FakeAttach(fixture.Root);
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var result = await client.CallAsync(UnityTools.ExecuteCSharp,
            new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("read.cs") }, TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal("return 123;", attach.LastCode);
    }

    [Fact]
    public async Task RecompileSendsOneRequestAndReportsCompilerErrors()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root)
        {
            Recompile = (_, _) => Task.FromResult(new JsonObject
            {
                ["state"] = "completed", ["succeeded"] = false, ["sequence"] = 4, ["errorCount"] = 1, ["warningCount"] = 0,
                ["errors"] = new JsonArray(new JsonObject { ["file"] = "A.cs", ["line"] = 3, ["column"] = 5, ["message"] = "CS1002" })
            })
        };
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var result = await client.CallAsync(UnityTools.Recompile,
            new Dictionary<string, JsonElement> { ["yieldTimeMs"] = JsonSerializer.SerializeToElement(500) },
            TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("CompilationFailed", result.ErrorCode);
        Assert.Contains("A.cs(3,5): CS1002", result.ErrorMessage);
        Assert.Equal(1, attach.RecompileCount);
        Assert.Equal(500, attach.LastYieldTimeMs);
        Assert.Equal(0, attach.ExecuteCount);
    }

    [Fact]
    public async Task RecompileStillRunningIsNotSuccessAndIsNotRequestedAgain()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root)
        {
            Recompile = (_, _) => Task.FromResult(new JsonObject { ["state"] = "running", ["phase"] = "reloading", ["sequence"] = 7 })
        };
        await using var client = new AttachToolClient(fixture.Root, null, attach);
        var result = await client.CallAsync(UnityTools.Recompile, null, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("RecompileStillRunning", result.ErrorCode);
        Assert.Equal(UnityTools.DefaultRecompileYieldMs, attach.LastYieldTimeMs);
        Assert.Equal(1, attach.RecompileCount);
    }

    private static Dictionary<string, JsonElement> Code() => new() { ["code"] = JsonSerializer.SerializeToElement("return 42;") };

    private sealed class FakeAttach(string project) : IAttachConnection
    {
        public IReadOnlyList<AttachTarget> Targets { get; set; } = [new(11, project, "Unity.exe")];
        public int ConnectCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public int LastPid { get; private set; }
        public string? LastCode { get; private set; }
        public Func<CancellationToken, Task<JsonObject>>? Execute { get; init; }
        public Func<int, CancellationToken, Task<JsonObject>>? Recompile { get; init; }
        public int RecompileCount { get; private set; }
        public int LastYieldTimeMs { get; private set; }
        public IReadOnlyList<AttachTarget> List() => Targets;
        public Task<JsonObject> ConnectAsync(int pid, CancellationToken cancellationToken)
        {
            ConnectCount++; LastPid = pid;
            return Task.FromResult(new JsonObject { ["state"] = "completed", ["result"] = new JsonObject { ["project"] = project, ["playing"] = false } });
        }
        public Task<JsonObject> ExecuteAsync(string code, JsonObject? args, CancellationToken cancellationToken)
        {
            ExecuteCount++; LastCode = code;
            return Execute?.Invoke(cancellationToken) ?? Task.FromResult(new JsonObject { ["state"] = "completed", ["result"] = 42 });
        }
        public Task<JsonObject> RecompileAsync(int yieldTimeMs, CancellationToken cancellationToken)
        {
            RecompileCount++; LastYieldTimeMs = yieldTimeMs;
            return Recompile?.Invoke(yieldTimeMs, cancellationToken) ?? Task.FromResult(new JsonObject
            {
                ["state"] = "completed", ["succeeded"] = true, ["sequence"] = 1, ["warningCount"] = 0, ["domainReloaded"] = true
            });
        }
        public UnityEditorState? PeekState(int pid) => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
