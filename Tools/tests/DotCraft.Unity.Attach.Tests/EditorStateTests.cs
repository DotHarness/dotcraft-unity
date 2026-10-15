using System.Text.Json.Nodes;
using DotCraft.Unity;
using Xunit;

public sealed class EditorStateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "editor-state-tests-" + Guid.NewGuid().ToString("N"));
    private readonly EditorStateFiles files = new();
    private readonly string connection;
    private DateTime clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private volatile bool running = true;

    public EditorStateTests()
    {
        Directory.CreateDirectory(root);
        connection = Path.Combine(root, "4242-1.json");
        WriteConnection("gen-1");
        WriteState("gen-1", 1, compile: Compile(3, succeeded: true));
        WriteLifecycle(1, 0);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void StateRecordWrittenByTheBridgeIsParsed()
    {
        Write(connection + ".state", """
            {"generation":"gen-1","domainEpoch":7,"playMode":"paused","compiling":false,"reloading":true,
             "compile":{"sequence":12,"startedUtc":"2026-10-15T08:00:00.123Z","finishedUtc":"2026-10-15T08:00:02Z",
             "succeeded":false,"errorCount":2,"warningCount":1,
             "errors":[{"file":"Assets/A.cs","line":4,"column":9,"message":"CS1002: ; expected"}]}}
            """);

        var state = Observe();

        Assert.Equal("Project", Path.GetFileName(state.Project));
        Assert.Equal("2022.3.0f1", state.Version);
        var bridge = Assert.IsType<UnityBridgeRecord>(state.Bridge);
        Assert.Equal(7UL, bridge.DomainEpoch);
        Assert.Equal("paused", bridge.PlayMode);
        Assert.True(bridge.Reloading);
        var compile = Assert.IsType<UnityCompileRecord>(bridge.Compile);
        Assert.Equal(12, compile.Sequence);
        Assert.False(compile.Succeeded);
        Assert.Equal(2, compile.ErrorCount);
        Assert.NotNull(compile.FinishedUtc);
        var error = Assert.Single(compile.Errors!);
        Assert.Equal(("Assets/A.cs", 4, 9, "CS1002: ; expected"), (error.File, error.Line, error.Column, error.Message));
    }

    [Fact]
    public void ConnectionStateFollowsRecordLifecycleAndProcess()
    {
        Assert.Equal(UnityConnectionState.Ready, Observe().Connection);
        WriteState("gen-1", 1, reloading: true);
        Assert.Equal(UnityConnectionState.Reloading, Observe().Connection);
        WriteLifecycle(2, 100);
        Assert.Equal(UnityConnectionState.Recovering, Observe().Connection);
        WriteLifecycle(2, 0);
        Assert.Equal(UnityConnectionState.Recovering, Observe().Connection);
        WriteLifecycle(2, 6);
        Assert.Equal(UnityConnectionState.RecoveryFailed, Observe().Connection);
        WriteState("gen-2", 2);
        WriteLifecycle(2, 0);
        Assert.Equal(UnityConnectionState.Recovering, Observe().Connection);
        WriteConnection("gen-2");
        Assert.Equal(UnityConnectionState.Ready, Observe().Connection);
        running = false;
        Assert.Equal(UnityConnectionState.Exited, Observe().Connection);
    }

    [Fact]
    public async Task CompileThatWasAlreadyFinishedIsNotTheRequestedOne()
    {
        var wait = Waiter();
        var result = wait.WaitAsync(3, 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);
        WriteState("gen-1", 1, compiling: true, compile: Compile(4, succeeded: null));
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);
        WriteState("gen-1", 1, compile: Compile(4, succeeded: true));

        var completed = await result;

        Assert.Equal("completed", completed["state"]!.GetValue<string>());
        Assert.True(completed["succeeded"]!.GetValue<bool>());
        Assert.Equal(4, completed["sequence"]!.GetValue<int>());
        Assert.False(completed["domainReloaded"]!.GetValue<bool>());
    }

    [Fact]
    public async Task FailedCompileReturnsItsErrors()
    {
        WriteState("gen-1", 1, compile: Compile(4, succeeded: false, errors: 25));

        var result = await Waiter().WaitAsync(3, 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("completed", result["state"]!.GetValue<string>());
        Assert.False(result["succeeded"]!.GetValue<bool>());
        Assert.Equal(25, result["errorCount"]!.GetValue<int>());
        var errors = result["errors"]!.AsArray();
        Assert.Equal(20, errors.Count);
        Assert.Equal("Assets/E0.cs", errors[0]!["file"]!.GetValue<string>());
    }

    [Fact]
    public async Task SuccessfulCompileWaitsForReloadRecovery()
    {
        var probes = 0;
        var ready = 0;
        var wait = new RecompileWait(Observe, _ =>
        {
            Interlocked.Increment(ref probes);
            return Task.FromResult(Volatile.Read(ref ready) == 1);
        }, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));
        WriteState("gen-1", 1, reloading: true, compile: Compile(4, succeeded: true));
        var result = wait.WaitAsync(3, 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);
        WriteLifecycle(2, 100);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        WriteState("gen-2", 2, compile: Compile(4, succeeded: true));
        WriteConnection("gen-2");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref probes));
        WriteLifecycle(2, 0);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(result.IsCompleted);
        Volatile.Write(ref ready, 1);

        var completed = await result;

        Assert.Equal("completed", completed["state"]!.GetValue<string>());
        Assert.True(completed["domainReloaded"]!.GetValue<bool>());
        Assert.True(Volatile.Read(ref probes) > 1);
    }

    [Fact]
    public async Task RecoveryFailureEndsTheWait()
    {
        WriteState("gen-1", 1, reloading: true, compile: Compile(4, succeeded: true));
        WriteLifecycle(2, 6);

        var result = await Waiter().WaitAsync(3, 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("failed", result["state"]!.GetValue<string>());
        Assert.Equal("UnityAttachBootstrapFailed", result["errorCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task WaitThatEndsFirstReturnsCurrentStateWithoutError()
    {
        WriteState("gen-1", 1, compiling: true, compile: Compile(4, succeeded: null));

        var result = await Waiter().WaitAsync(3, 1, TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.Equal("running", result["state"]!.GetValue<string>());
        Assert.Equal("compiling", result["phase"]!.GetValue<string>());
        Assert.Null(result["error"]);
    }

    private RecompileWait Waiter() => new(Observe, _ => Task.FromResult(true),
        TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));

    private UnityEditorState Observe() => files.Read(connection, 4242, new DateTime(1, DateTimeKind.Utc), running);

    private void WriteConnection(string generation) => Write(connection, new JsonObject
    {
        ["protocol"] = 1,
        ["generation"] = generation,
        ["project"] = Path.Combine(root, "Project"),
        ["version"] = "2022.3.0f1"
    }.ToJsonString());

    private void WriteState(string generation, ulong epoch, bool compiling = false, bool reloading = false, JsonObject? compile = null) =>
        Write(connection + ".state", new JsonObject
        {
            ["generation"] = generation,
            ["domainEpoch"] = epoch,
            ["playMode"] = "edit",
            ["compiling"] = compiling,
            ["reloading"] = reloading,
            ["compile"] = compile ?? Compile(3, succeeded: true)
        }.ToJsonString());

    private void WriteLifecycle(ulong epoch, int stage) =>
        Write(connection + ".lifecycle", $"{{\"observer\":true,\"epoch\":{epoch},\"stage\":{stage}}}");

    private static JsonObject Compile(int sequence, bool? succeeded, int errors = 0) => new()
    {
        ["sequence"] = sequence,
        ["startedUtc"] = "2026-10-15T08:00:00Z",
        ["finishedUtc"] = succeeded is null ? null : "2026-10-15T08:00:01Z",
        ["succeeded"] = succeeded,
        ["errorCount"] = errors,
        ["warningCount"] = 0,
        ["errors"] = new JsonArray(Enumerable.Range(0, Math.Min(errors, 25)).Select(index => (JsonNode)new JsonObject
        {
            ["file"] = $"Assets/E{index}.cs",
            ["line"] = index + 1,
            ["column"] = 1,
            ["message"] = "CS0103: missing"
        }).ToArray())
    };

    private void Write(string path, string content)
    {
        File.WriteAllText(path, content);
        clock = clock.AddSeconds(1);
        File.SetLastWriteTimeUtc(path, clock);
    }
}
