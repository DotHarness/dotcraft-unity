using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity.Cli;

internal sealed class AttachToolClient(string projectRoot, int? pid, IAttachConnection? connection = null)
    : IUnityToolClient, IAsyncDisposable
{
    private readonly IAttachConnection _connection = connection ?? new AttachConnection();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private int? _selectedPid;

    public IReadOnlyList<AttachTarget> ListTargets() => _connection.List();

    public UnityEditorState? PeekState(int targetPid) => _connection.PeekState(targetPid);

    public string? ValidateTarget()
    {
        var targets = ListTargets().Where(t => SameProject(t.Project, projectRoot) && (!pid.HasValue || t.Pid == pid)).ToArray();
        if (targets.Length == 1) { _selectedPid = targets[0].Pid; return null; }
        return targets.Length == 0
            ? "No matching Unity Editor is available for this project. Open the project or specify its PID with --pid."
            : "Multiple Unity Editors match this project. Select one with --pid: " + string.Join(", ", targets.Select(t => t.Pid));
    }

    public async Task<UnityToolGatewayResult> CallAsync(string name, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken, string? sessionId = null)
    {
        if (name is not (GatewayConstants.ExecuteCSharpToolName or GatewayConstants.RecompileToolName))
            return UnityBackendSession.Failure(name, "ToolNotFound", "Project tools require the Gateway backend.");
        var watch = Stopwatch.StartNew();
        try
        {
            var input = arguments ?? new Dictionary<string, JsonElement>();
            return name == GatewayConstants.RecompileToolName
                ? await RecompileAsync(name, input, watch, cancellationToken).ConfigureAwait(false)
                : await ExecuteAsync(name, input, watch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnityTargetException exception)
        {
            return UnityBackendSession.Failure(name, exception.Code, exception.Message);
        }
        catch (PlatformNotSupportedException exception)
        {
            return UnityBackendSession.Failure(name, "UnityAttachUnsupported", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException
            or TimeoutException or System.Net.Sockets.SocketException or System.ComponentModel.Win32Exception)
        {
            return UnityBackendSession.Failure(name, "UnityAttachFailed", exception.Message);
        }
    }

    private async Task<UnityToolGatewayResult> ExecuteAsync(string name, IDictionary<string, JsonElement> input,
        Stopwatch watch, CancellationToken cancellationToken)
    {
        var code = Text(input, "code");
        var path = Text(input, "path");
        if ((code is null) == (path is null))
            return UnityBackendSession.Failure(name, "InvalidArguments", "Specify exactly one of code or path.");
        var mode = Text(input, "mode") ?? "editor";
        if (mode is not ("editor" or "playmode"))
            return UnityBackendSession.Failure(name, "InvalidMode", "mode must be editor or playmode.");
        var args = input.TryGetValue("args", out var value) ? JsonNode.Parse(value.GetRawText()) as JsonObject : null;
        if (input.ContainsKey("args") && args is null)
            return UnityBackendSession.Failure(name, "InvalidArguments", "args must be a JSON object.");
        if (path != null) code = await File.ReadAllTextAsync(Path.GetFullPath(path, projectRoot), cancellationToken);
        if (string.IsNullOrWhiteSpace(code))
            return UnityBackendSession.Failure(name, "InvalidArguments", "C# code must not be empty.");

        var (metadata, failure) = await ConnectAsync(name, cancellationToken).ConfigureAwait(false);
        if (failure != null) return failure;
        if (mode == "playmode" && metadata!["result"]?["playing"]?.GetValue<bool>() != true)
            return UnityBackendSession.Failure(name, "UnityNotInPlayMode", "Unity must already be in Play Mode.");

        var result = await _connection.ExecuteAsync(code!, args, cancellationToken).ConfigureAwait(false);
        var state = result["state"]?.GetValue<string>();
        if (state != "completed")
            return UnityBackendSession.Failure(name, result["errorCode"]?.GetValue<string>() ?? (state switch
            {
                "lost" => "UnityExecutionLost", "unknown" => "UnityOutcomeUnknown",
                "cancelled" => "Cancelled", _ => "ExecutionException"
            }), result["error"]?.ToString() ?? $"Unity execution is {state}. Do not replay automatically.");
        return new UnityToolGatewayResult
        {
            Name = name, Success = true, DurationMs = watch.ElapsedMilliseconds,
            Result = JsonSerializer.SerializeToElement(new { mode, returnValue = result["result"],
                diagnostics = Array.Empty<object>(), logs = Array.Empty<object>(), durationMs = watch.ElapsedMilliseconds }),
            Text = result["result"]?.ToJsonString() ?? "null"
        };
    }

    private async Task<UnityToolGatewayResult> RecompileAsync(string name, IDictionary<string, JsonElement> input,
        Stopwatch watch, CancellationToken cancellationToken)
    {
        var yieldTimeMs = GatewayConstants.DefaultRecompileYieldMs;
        if (input.TryGetValue("yieldTimeMs", out var yieldValue) && yieldValue.ValueKind != JsonValueKind.Null)
        {
            if (!yieldValue.TryGetInt32(out yieldTimeMs) || yieldTimeMs < 0 || yieldTimeMs > GatewayConstants.MaxRecompileYieldMs)
                return UnityBackendSession.Failure(name, "InvalidArguments",
                    $"yieldTimeMs must be an integer from 0 to {GatewayConstants.MaxRecompileYieldMs}.");
        }
        if (input.Keys.Any(key => key != "yieldTimeMs"))
            return UnityBackendSession.Failure(name, "InvalidArguments", "unity_recompile accepts only yieldTimeMs.");

        var (_, failure) = await ConnectAsync(name, cancellationToken).ConfigureAwait(false);
        if (failure != null) return failure;

        var result = await _connection.RecompileAsync(yieldTimeMs, cancellationToken).ConfigureAwait(false);
        var state = result["state"]?.GetValue<string>();
        var element = JsonSerializer.SerializeToElement(result);
        switch (state)
        {
            case "completed" when result["succeeded"]?.GetValue<bool>() == true:
                var reloaded = result["domainReloaded"]?.GetValue<bool>() == true;
                return new UnityToolGatewayResult
                {
                    Name = name, Success = true, DurationMs = watch.ElapsedMilliseconds, Result = element,
                    Text = reloaded ? "Scripts compiled and the Domain Reload finished." : "Scripts compiled; the Editor settled without a Domain Reload."
                };
            case "completed":
                var errors = result["errors"]?.AsArray().Select(error =>
                    $"{error?["file"]}({error?["line"]},{error?["column"]}): {error?["message"]}") ?? [];
                return Failure(name, "CompilationFailed",
                    $"Compilation failed with {result["errorCount"]} error(s).\n" + string.Join("\n", errors), element, watch);
            case "running":
                return Failure(name, "RecompileStillRunning",
                    $"Recompilation is still {result["phase"]} (baseline sequence {result["sequence"]}). Do not request it again; " +
                    "watch status for a newer finished compilation and check whether it succeeded. If its connection is not " +
                    "Ready, run one read-only exec probe to reattach before continuing.", element, watch);
            case "unknown":
                return Failure(name, "UnityOutcomeUnknown",
                    "The recompile request may or may not have reached Unity. Read status instead of requesting it again.", element, watch);
            default:
                return Failure(name, result["errorCode"]?.GetValue<string>() ?? "RecompileFailed",
                    result["error"]?.ToString() ?? $"Recompile is {state}.", element, watch);
        }
    }

    private async Task<(JsonObject? Metadata, UnityToolGatewayResult? Failure)> ConnectAsync(string name, CancellationToken cancellationToken)
    {
        JsonObject metadata;
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_selectedPid.HasValue && ValidateTarget() is { } error)
                return (null, UnityBackendSession.Failure(name, "UnityUnavailable", error));
            metadata = await _connection.ConnectAsync(_selectedPid!.Value, cancellationToken).ConfigureAwait(false);
        }
        finally { _connectGate.Release(); }

        if (metadata["state"]?.GetValue<string>() != "completed")
            return (null, UnityBackendSession.Failure(name, "UnityUnavailable", metadata.ToJsonString()));
        if (!SameProject(metadata["result"]?["project"]?.GetValue<string>(), projectRoot))
            return (null, UnityBackendSession.Failure(name, "UnityTargetMismatch", "The attached Editor belongs to a different project."));
        return (metadata, null);
    }

    private static UnityToolGatewayResult Failure(string name, string code, string message, JsonElement result, Stopwatch watch) => new()
    {
        Name = name, Success = false, ErrorCode = code, ErrorMessage = message, Text = message,
        Result = result, DurationMs = watch.ElapsedMilliseconds
    };

    public Task<ClientPresenceAck?> PostPresenceAsync(ClientPresenceRequest presence, CancellationToken cancellationToken) =>
        Task.FromResult<ClientPresenceAck?>(null);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }

    private static string? Text(IDictionary<string, JsonElement> input, string name) =>
        input.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static bool SameProject(string? left, string right) => left != null &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
