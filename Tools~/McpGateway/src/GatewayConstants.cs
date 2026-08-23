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
    public const string ExecuteCSharpToolName = "unity_execute_csharp";
    public const string DiscoveryRelativePath = "UserSettings/DotCraft/dotcraft-unity.json";
    public const string ManifestRelativePath = "UserSettings/DotCraft/tools.json";
}
