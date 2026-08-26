using ModelContextProtocol.Server;

namespace DotCraft.Unity.McpGateway;

/// <summary>What this gateway process knows about the single MCP client it serves.</summary>
internal sealed class ClientPresenceState
{
    private readonly object _gate = new();
    private string? _name;
    private string? _title;
    private string? _version;

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public bool HasClient
    {
        get
        {
            lock (_gate)
                return _name is not null;
        }
    }

    /// <summary>The SDK exposes the client identity only after initialize.</summary>
    public void Observe(McpServer? server)
    {
        var info = server?.ClientInfo;
        if (info is null)
            return;

        lock (_gate)
        {
            _name = info.Name;
            _title = info.Title;
            _version = info.Version;
        }
    }

    public ClientPresenceRequest BuildRequest(string state)
    {
        lock (_gate)
        {
            return new ClientPresenceRequest
            {
                State = state,
                SessionId = SessionId,
                ProcessId = Environment.ProcessId,
                Client = _name is null
                    ? null
                    : new ClientPresenceClientInfo { Name = _name, Title = _title, Version = _version }
            };
        }
    }
}
