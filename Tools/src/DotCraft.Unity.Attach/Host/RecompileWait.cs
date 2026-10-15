using System.Diagnostics;
using System.Text.Json.Nodes;

namespace DotCraft.Unity;

internal sealed class RecompileWait(
    Func<UnityEditorState> observe,
    Func<CancellationToken, Task<bool>> bridgeReady,
    TimeSpan poll,
    TimeSpan settle)
{
    public async Task<JsonObject> WaitAsync(int sequence, ulong domainEpoch, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan? settledAt = null;
        var phase = "compiling";
        while (true)
        {
            var state = observe();
            if (!state.Running)
                throw new UnityTargetException("UnityTargetUnavailable", "The selected Unity Editor exited before the recompilation finished.");
            if (state.Bridge is { Compile: { Sequence: var compiled, FinishedUtc: not null } compile } bridge && compiled > sequence)
            {
                if (compile.Succeeded != true) return Failed(compile);
                var lifecycle = state.Lifecycle;
                if (bridge.Reloading || bridge.DomainEpoch > domainEpoch
                    || lifecycle is not null && (lifecycle.Epoch > domainEpoch || lifecycle.Stage == UnityLifecycle.Dispatching))
                {
                    phase = "reloading";
                    settledAt = null;
                    if (state.Connection == UnityConnectionState.RecoveryFailed)
                        return new JsonObject
                        {
                            ["state"] = "failed",
                            ["errorCode"] = "UnityAttachBootstrapFailed",
                            ["error"] = $"Scripts compiled, but the bridge did not recover after the Domain Reload (bootstrap status {lifecycle!.Stage})."
                        };
                    if (state.Connection == UnityConnectionState.Ready && bridge.DomainEpoch > domainEpoch
                        && lifecycle?.Settled != false && await bridgeReady(cancellationToken))
                        return Succeeded(compile, domainReloaded: true);
                }
                else if (bridge.Compiling)
                {
                    phase = "compiling";
                    settledAt = null;
                }
                else
                {
                    phase = "settling";
                    settledAt ??= elapsed.Elapsed;
                    if (elapsed.Elapsed - settledAt >= settle) return Succeeded(compile, domainReloaded: false);
                }
            }
            if (elapsed.Elapsed >= timeout)
                return new JsonObject { ["state"] = "running", ["phase"] = phase, ["sequence"] = sequence };
            await Task.Delay(poll, cancellationToken);
        }
    }

    private static JsonObject Succeeded(UnityCompileRecord compile, bool domainReloaded) => new()
    {
        ["state"] = "completed",
        ["succeeded"] = true,
        ["sequence"] = compile.Sequence,
        ["warningCount"] = compile.WarningCount,
        ["domainReloaded"] = domainReloaded
    };

    private static JsonObject Failed(UnityCompileRecord compile) => new()
    {
        ["state"] = "completed",
        ["succeeded"] = false,
        ["sequence"] = compile.Sequence,
        ["errorCount"] = compile.ErrorCount,
        ["warningCount"] = compile.WarningCount,
        ["errors"] = new JsonArray((compile.Errors ?? []).Take(20).Select(error => (JsonNode)new JsonObject
        {
            ["file"] = error.File,
            ["line"] = error.Line,
            ["column"] = error.Column,
            ["message"] = error.Message
        }).ToArray())
    };
}
