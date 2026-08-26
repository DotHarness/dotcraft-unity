using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Unity.McpGateway;

internal sealed class UnityToolGatewayDiscovery
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = string.Empty;

    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

internal sealed class ToolManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public string Revision { get; set; } = string.Empty;

    [JsonPropertyName("tools")]
    public List<ToolManifestEntry> Tools { get; set; } = [];
}

internal sealed class ToolManifestEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }
}

internal sealed class UnityToolGatewayResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }
}

internal sealed class ClientPresenceClientInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal sealed class ClientPresenceRequest
{
    [JsonPropertyName("state")]
    public string State { get; set; } = GatewayConstants.PresenceStateOnline;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }

    /// <summary>Null until the client identifies itself during initialize.</summary>
    [JsonPropertyName("client")]
    public ClientPresenceClientInfo? Client { get; set; }
}

internal sealed class ClientPresenceAck
{
    [JsonPropertyName("heartbeatSeconds")]
    public int HeartbeatSeconds { get; set; }
}
