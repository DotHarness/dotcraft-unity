using System;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.Settings;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class McpGatewayRuntime
    {
        private static readonly Lazy<McpGatewayRuntime> LazyInstance = new(() => new McpGatewayRuntime());
        private readonly ToolGatewayLocalServer _server;

        private McpGatewayRuntime()
            : this(new ToolGatewayLocalServer())
        {
        }

        internal McpGatewayRuntime(ToolGatewayLocalServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        public static McpGatewayRuntime Instance => LazyInstance.Value;
        public bool IsRunning => _server.IsRunning;
        public string Endpoint => _server.ListenUrl.TrimEnd('/') + ToolGatewayMcpProtocol.Paths.Mcp;
        public string LastError => _server.LastError;

        internal event Action StatusChanged;

        public void ApplySettings()
        {
            try
            {
                if (DotCraftSettings.Instance.EnableMcpGateway)
                    _server.Start();
                else
                    _server.Stop();
            }
            finally
            {
                NotifyStatusChanged();
            }
        }

        public void Restart()
        {
            try
            {
                _server.Restart();
            }
            finally
            {
                NotifyStatusChanged();
            }
        }

        public void Shutdown()
        {
            try
            {
                _server.Stop();
            }
            finally
            {
                NotifyStatusChanged();
            }
        }

        private void NotifyStatusChanged()
        {
            MainThreadDispatcher.RunOrEnqueue(() => StatusChanged?.Invoke());
        }
    }
}
