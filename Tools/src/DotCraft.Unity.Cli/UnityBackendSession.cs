using System.Text.Json;

namespace DotCraft.Unity.Cli;

/// <summary>Selects a transport before dispatch and never replays a failed call.</summary>
internal sealed class UnityBackendSession : IUnityToolClient, IAsyncDisposable
{
    private readonly ProjectStateStore _store;
    private readonly string _requestedBackend;
    private readonly int? _pid;
    private readonly UnityToolGatewayClient _gateway;
    private readonly AttachToolClient _attach;
    private readonly SemaphoreSlim _selection = new(1, 1);
    private string? _selectedBackend;

    public UnityBackendSession(ProjectStateStore store, string backend = "auto", int? pid = null,
        IAttachConnection? attach = null, HttpClient? httpClient = null)
    {
        _store = store;
        _requestedBackend = backend;
        _pid = pid;
        _gateway = new UnityToolGatewayClient(store, httpClient);
        _attach = new AttachToolClient(store.ProjectRoot, pid, attach);
    }

    public string Backend => _selectedBackend ?? SelectBackend(_requestedBackend,
        File.Exists(_store.DiscoveryPath), File.Exists(_store.ManifestPath));

    internal static string SelectBackend(string requested, bool discoveryExists, bool manifestExists) =>
        requested == "auto" ? discoveryExists || manifestExists ? "gateway" : "attach" : requested;

    public ToolManifest ReadManifest(out string source)
    {
        if (Backend == "gateway") return _store.ReadManifestOrDefault(out source);
        source = "attach";
        var manifest = DefaultManifest.Create();
        manifest.Revision = "attach:" + GatewayConstants.ProductVersion;
        return manifest;
    }

    public IReadOnlyList<AttachTarget> ListAttachTargets() => _attach.ListTargets();

    public async Task<UnityToolGatewayResult> CallAsync(string name, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken, string? sessionId = null)
    {
        await _selection.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_selectedBackend is null)
            {
                var candidate = Backend;
                if (candidate == "gateway")
                {
                    // Keep the selected project transport across reloads and version changes.
                    _selectedBackend = candidate;
                    var discovery = _store.ReadLiveDiscovery(out var error);
                    if (discovery is null) return Failure(name, "UnityUnavailable", error!);
                    if (_pid.HasValue && discovery.ProcessId != _pid)
                        return Failure(name, "UnityTargetMismatch", "The Gateway belongs to a different Unity process.");
                }
                else
                {
                    if (name != GatewayConstants.ExecuteCSharpToolName)
                        return Failure(name, "ToolNotFound", "Attach supports unity_execute_csharp; project tools require the Gateway backend.");
                    var error = _attach.ValidateTarget();
                    if (error != null) return Failure(name, "UnityUnavailable", error);
                }
                // Freeze before any remote dispatch, including an uncertain bootstrap.
                _selectedBackend = candidate;
            }
        }
        finally { _selection.Release(); }

        return await (_selectedBackend == "gateway" ? (IUnityToolClient)_gateway : _attach)
            .CallAsync(name, arguments, cancellationToken, sessionId).ConfigureAwait(false);
    }

    public Task<ClientPresenceAck?> PostPresenceAsync(ClientPresenceRequest presence, CancellationToken cancellationToken) =>
        Backend == "gateway" ? _gateway.PostPresenceAsync(presence, cancellationToken)
            : Task.FromResult<ClientPresenceAck?>(null);

    public async ValueTask DisposeAsync()
    {
        await _attach.DisposeAsync().ConfigureAwait(false);
        _selection.Dispose();
    }

    internal static UnityToolGatewayResult Failure(string name, string code, string message) => new()
    {
        Name = name, Success = false, ErrorCode = code, ErrorMessage = message, Text = message
    };
}
