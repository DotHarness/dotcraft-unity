namespace DotCraft.Editor.McpSetup
{
    internal sealed class CodexMcpConfigProvider : McpClientConfigProviderBase
    {
        public override string DisplayName => "Codex";

        public override string RelativePath => ".codex/config.toml";

        public override string SkillRelativePath => ".agents/skills/dotcraft-unity";

        public override string GetSetupHint(McpInstallOptions options)
        {
            return "Start Codex in the project root; setup also installs the project skill under .agents/skills.";
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
