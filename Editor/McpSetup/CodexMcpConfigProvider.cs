namespace DotCraft.Editor.McpSetup
{
    internal sealed class CodexMcpConfigProvider : McpClientConfigProviderBase
    {
        public override string DisplayName => "Codex";

        public override string RelativePath => ".codex/config.toml";

        public override string GetSetupHint(McpInstallOptions options)
        {
            return options.UseCodexReadOnly
                ? "Codex will load only the read-only Unity tools from this server."
                : "Start Codex in the project root; tools use prompt approval by default.";
        }

        public override McpPatchPreview Preview(string projectRoot, McpInstallOptions options)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            return TomlBlockPatcher.PreviewInstall(path, before, options);
        }

        protected override McpPatchPreview PreviewUninstall(string path, string before) =>
            TomlBlockPatcher.PreviewUninstall(path, before);
    }
}
