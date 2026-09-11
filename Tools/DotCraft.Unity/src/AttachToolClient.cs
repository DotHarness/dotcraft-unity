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
        if (name != GatewayConstants.ExecuteCSharpToolName)
            return UnityBackendSession.Failure(name, "ToolNotFound", "Project tools require the Gateway backend.");
        var watch = Stopwatch.StartNew();
        try
        {
            var input = arguments ?? new Dictionary<string, JsonElement>();
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

            JsonObject metadata;
            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_selectedPid.HasValue && ValidateTarget() is { } error)
                    return UnityBackendSession.Failure(name, "UnityUnavailable", error);
                metadata = await _connection.ConnectAsync(_selectedPid!.Value, cancellationToken).ConfigureAwait(false);
            }
            finally { _connectGate.Release(); }

            if (metadata["state"]?.GetValue<string>() != "completed")
                return UnityBackendSession.Failure(name, "UnityUnavailable", metadata.ToJsonString());
            if (!SameProject(metadata["result"]?["project"]?.GetValue<string>(), projectRoot))
                return UnityBackendSession.Failure(name, "UnityTargetMismatch", "The attached Editor belongs to a different project.");
            if (mode == "playmode" && metadata["result"]?["playing"]?.GetValue<bool>() != true)
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
