using System;
using DotCraft.Editor.Settings;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class McpGatewayRuntime
    {
        private static readonly Lazy<McpGatewayRuntime> LazyInstance = new(() => new McpGatewayRuntime());
        private readonly ToolGatewayLocalServer _server = new();

        public static McpGatewayRuntime Instance => LazyInstance.Value;
        public bool IsRunning => _server.IsRunning;
        public string Endpoint => _server.ListenUrl.TrimEnd('/') + ToolGatewayMcpProtocol.Paths.Mcp;
        public string LastError => _server.LastError;

        public void ApplySettings()
        {
            if (DotCraftSettings.Instance.EnableMcpGateway)
                _server.Start();
            else
                _server.Stop();
        }

        public void Restart() => _server.Restart();
        public void Shutdown() => _server.Stop();
    }
}
