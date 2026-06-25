using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class CursorMcpConfigProvider : McpClientConfigProviderBase
    {
        public override string DisplayName => "Cursor";

        public override string RelativePath => ".cursor/mcp.json";

        public override string GetSetupHint(McpInstallOptions options)
        {
            return options.UseCodexReadOnly
                ? "Cursor uses its own MCP approval and tool controls; read-only allowlists are not written here."
                : "Open Cursor from the project root and verify the server in Cursor MCP settings.";
        }

        public override McpPatchPreview Preview(string projectRoot, McpInstallOptions options)
        {
            var path = ResolvePath(projectRoot);
            var before = ReadExisting(path);
            var serverConfig = new JObject
            {
                ["url"] = options.Endpoint
            };
            return JsonConfigPatcher.PreviewInstall(path, before, McpInstallOptions.ServerName, serverConfig);
        }

        protected override McpPatchPreview PreviewUninstall(string path, string before) =>
            JsonConfigPatcher.PreviewUninstall(path, before, McpInstallOptions.ServerName);
    }
}
