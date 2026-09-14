using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity.Cli.Tests;

public sealed class BackendSelectionTests
{
    [Fact]
    public async Task ReadOnlyInspectionDoesNotConnectOrExposeProjectToolsForAttach()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root);
        await using var session = new UnityBackendSession(new(fixture.Root), "attach", attach: attach);
        var manifest = session.ReadManifest(out var source);
        Assert.Equal("attach", source);
        Assert.Equal(GatewayConstants.ExecuteCSharpToolName, Assert.Single(manifest.Tools).Name);
        Assert.Single(session.ListAttachTargets());
        Assert.Equal(0, attach.ConnectCount);
        var result = await session.CallAsync("project_custom", null, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("ToolNotFound", result.ErrorCode);
        Assert.Equal(0, attach.ConnectCount);
    }

    [Fact]
    public async Task AutoUsesExistingGatewayAndNeverAttachesAfterAuthenticationFailure()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root);
        await using var session = new UnityBackendSession(new(fixture.Root), attach: attach);
        var reply = fixture.ReplyOnceAsync(status: 401);
        var result = await session.CallAsync("custom", null, TestContext.Current.CancellationToken);
        await reply;
        Assert.Equal("UnityUnavailable", result.ErrorCode);
        File.Delete(Path.Combine(fixture.Root, GatewayConstants.DiscoveryRelativePath));
        Assert.Equal("gateway", session.Backend);
        result = await session.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal(0, attach.ConnectCount);
    }

    [Fact]
    public async Task DifferentProductVersionWithSameProtocolUsesGateway()
    {
        using var fixture = new CliFixture();
        fixture.WriteDiscovery(productVersion: "999.0.0");
        var attach = new FakeAttach(fixture.Root);
        await using var session = new UnityBackendSession(new(fixture.Root), attach: attach);
        var reply = fixture.ReplyOnceAsync();

        var result = await session.CallAsync("custom", null, TestContext.Current.CancellationToken);

        await reply;
        Assert.True(result.Success);
        Assert.Equal("gateway", session.Backend);
        Assert.Equal(0, attach.ConnectCount);
    }

    [Fact]
    public async Task StaleGatewayIsReportedWithoutInjecting()
    {
        using var fixture = new CliFixture();
        fixture.WriteDiscovery(protocolVersion: GatewayConstants.ProtocolVersion + 1);
        var attach = new FakeAttach(fixture.Root);
        await using var session = new UnityBackendSession(new(fixture.Root), attach: attach);
        var result = await session.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Contains("protocol mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        File.Delete(Path.Combine(fixture.Root, GatewayConstants.DiscoveryRelativePath));
        Assert.Equal("gateway", session.Backend);
        Assert.False((await session.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(0, attach.ConnectCount);
    }

    [Fact]
    public async Task AutoAttachesOnlyToUniqueProjectAndPreservesResultEnvelope()
    {
        using var fixture = new CliFixture();
        File.Delete(Path.Combine(fixture.Root, GatewayConstants.DiscoveryRelativePath));
        var attach = new FakeAttach(fixture.Root);
        await using var session = new UnityBackendSession(new(fixture.Root), attach: attach);
        var result = await session.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal(42, result.Result!.Value.GetProperty("returnValue").GetInt32());
        Assert.Equal("editor", result.Result.Value.GetProperty("mode").GetString());
        Assert.Equal(1, attach.ConnectCount);
        Assert.Equal(1, attach.ExecuteCount);
        fixture.WriteDiscovery();
        Assert.Equal("attach", session.Backend);
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
        await using var session = new UnityBackendSession(new(fixture.Root), "attach", attach: attach);

        var result = await session.CallAsync(
            GatewayConstants.ExecuteCSharpToolName,
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
        await using var session = new UnityBackendSession(new(fixture.Root), "attach", attach: attach);
        var result = await session.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), TestContext.Current.CancellationToken);
        Assert.Contains("Multiple", result.ErrorMessage);
        Assert.Equal(0, attach.ConnectCount);
        await using var selected = new UnityBackendSession(new(fixture.Root), "attach", 12, attach);
        Assert.True((await selected.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(12, attach.LastPid);
    }

    [Fact]
    public async Task PlayModeDoesNotEnterPlayModeOrExecuteWhenEditorIsStopped()
    {
        using var fixture = new CliFixture();
        var attach = new FakeAttach(fixture.Root);
        await using var session = new UnityBackendSession(new(fixture.Root), "attach", attach: attach);
        var args = Code();
        args["mode"] = JsonSerializer.SerializeToElement("playmode");
        var result = await session.CallAsync(GatewayConstants.ExecuteCSharpToolName, args, TestContext.Current.CancellationToken);
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
        await using var session = new UnityBackendSession(new(fixture.Root), "attach", attach: attach);
        var pending = session.CallAsync(GatewayConstants.ExecuteCSharpToolName, Code(), cancellation.Token);
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
        await using var session = new UnityBackendSession(new(fixture.Root), "attach", attach: attach);
        var result = await session.CallAsync(GatewayConstants.ExecuteCSharpToolName,
            new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("read.cs") }, TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal("return 123;", attach.LastCode);
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
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
