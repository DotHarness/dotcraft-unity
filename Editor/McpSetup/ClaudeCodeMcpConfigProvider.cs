using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class ClaudeCodeMcpConfigProvider : McpClientConfigProviderBase
    {
        public override string DisplayName => "Claude Code";

        public override string RelativePath => ".mcp.json";

        public override string GetSetupHint(McpInstallOptions options)
        {
            return "Start Claude Code in the project root and approve the project-scoped MCP server when prompted.";
        }

        public override McpPatchPreview Preview(string projectRoot, McpInstallOptions options)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            var serverConfig = new JObject
            {
                ["type"] = "http",
                ["url"] = options.Endpoint
            };
            return JsonConfigPatcher.PreviewInstall(path, before, McpInstallOptions.ServerName, serverConfig);
        }

        protected override McpPatchPreview PreviewUninstall(string path, string before) =>
            JsonConfigPatcher.PreviewUninstall(path, before, McpInstallOptions.ServerName);
    }
}
