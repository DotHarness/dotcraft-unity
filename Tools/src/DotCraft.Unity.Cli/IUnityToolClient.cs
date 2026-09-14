using System.Text.Json;

namespace DotCraft.Unity.Cli;

internal interface IUnityToolClient
{
    Task<UnityToolGatewayResult> CallAsync(string name, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken, string? sessionId = null);

    Task<ClientPresenceAck?> PostPresenceAsync(ClientPresenceRequest presence, CancellationToken cancellationToken);
}
