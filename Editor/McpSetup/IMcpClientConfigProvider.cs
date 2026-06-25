namespace DotCraft.Editor.McpSetup
{
    internal interface IMcpClientConfigProvider
    {
        string DisplayName { get; }

        string RelativePath { get; }

        bool IsRecommendedByDefault { get; }

        string GetSetupHint(McpInstallOptions options);

        McpPatchPreview Preview(string projectRoot, McpInstallOptions options);

        McpInstallResult Install(string projectRoot, McpInstallOptions options);

        McpInstallResult Uninstall(string projectRoot);
    }
}
