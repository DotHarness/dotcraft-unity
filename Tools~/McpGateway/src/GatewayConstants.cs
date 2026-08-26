using System.Reflection;

namespace DotCraft.Unity.McpGateway;

internal static class GatewayConstants
{
    public static readonly string PackageVersion =
        typeof(GatewayConstants).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? throw new InvalidOperationException("Gateway version metadata is missing.");
    public const string McpSdkVersion = "2.2.0";
    public const string RuntimeIdentifier = "win-x64";
    public const int SchemaVersion = 1;
    public const string ToolGatewayTokenHeader = "X-DotCraft-Unity-Token";
    public const string ToolGatewaySessionHeader = "X-DotCraft-Unity-Session";
    public const string ToolGatewayCallPath = "/dotcraft-unity/call";
    public const string ExecuteCSharpToolName = "unity_execute_csharp";
    public const string DiscoveryRelativePath = "UserSettings/DotCraft/dotcraft-unity.json";
    public const string ManifestRelativePath = "UserSettings/DotCraft/tools.json";

    public const int DefaultHeartbeatSeconds = 15;
    public const int MinHeartbeatSeconds = 5;
    public const int MaxHeartbeatSeconds = 120;

    public const int PresenceRetrySeconds = 3;

    public const string PresenceStateOnline = "online";
    public const string PresenceStateClosing = "closing";
}
