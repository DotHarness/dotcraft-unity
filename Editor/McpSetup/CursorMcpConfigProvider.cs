using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class CursorMcpConfigProvider : McpClientConfigProviderBase
    {
        public override string DisplayName => "Cursor";

        public override string RelativePath => ".cursor/mcp.json";

        public override string SkillRelativePath => ".agents/skills/dotcraft-unity";

        public override string GetSetupHint(McpInstallOptions options)
        {
            return "Open Cursor from the project root; setup also installs the shared project skill under .agents/skills.";
        }

        public override McpPatchPreview Preview(string projectRoot, McpInstallOptions options)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            var serverConfig = new JObject
            {
                ["command"] = options.Command,
                ["args"] = new JArray("mcp", "--project-root", options.ProjectRoot)
            };
            return JsonConfigPatcher.PreviewInstall(path, before, McpInstallOptions.ServerName, serverConfig);
        }

        protected override McpPatchPreview PreviewUninstall(string path, string before) =>
            JsonConfigPatcher.PreviewUninstall(path, before, McpInstallOptions.ServerName);
    }
}
