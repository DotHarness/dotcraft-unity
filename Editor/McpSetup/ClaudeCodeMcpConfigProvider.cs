using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class ClaudeCodeMcpConfigProvider : McpClientConfigProviderBase
    {
        public override string DisplayName => "Claude Code";

        public override string RelativePath => ".mcp.json";

        public override string SkillRelativePath => ".claude/skills/dotcraft-unity";

        public override string GetSetupHint(McpInstallOptions options)
        {
            return "Start Claude Code in the project root; setup also installs the project skill under .claude/skills.";
        }

        public override McpPatchPreview Preview(string projectRoot, McpInstallOptions options)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            var serverConfig = new JObject
            {
                ["type"] = "stdio",
                ["command"] = options.Command,
                ["args"] = new JArray("--project-root", options.ProjectRoot)
            };
            return JsonConfigPatcher.PreviewInstall(path, before, McpInstallOptions.ServerName, serverConfig);
        }

        protected override McpPatchPreview PreviewUninstall(string path, string before) =>
            JsonConfigPatcher.PreviewUninstall(path, before, McpInstallOptions.ServerName);
    }
}
