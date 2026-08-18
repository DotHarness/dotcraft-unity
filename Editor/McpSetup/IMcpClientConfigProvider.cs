namespace DotCraft.Editor.McpSetup
{
    internal interface IMcpClientConfigProvider
    {
        string DisplayName { get; }

        string RelativePath { get; }

        string SkillRelativePath { get; }

        bool IsRecommendedByDefault { get; }

        bool IsConfigured(string projectRoot);

        string GetSetupHint(McpInstallOptions options);

        McpPatchPreview Preview(string projectRoot, McpInstallOptions options);

        McpInstallResult Install(string projectRoot, McpInstallOptions options);

        McpInstallResult Uninstall(string projectRoot);
    }
}
