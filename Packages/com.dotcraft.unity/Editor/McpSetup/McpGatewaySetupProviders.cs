namespace DotCraft.Editor.McpSetup
{
    internal static class McpGatewaySetupProviders
    {
        public static IMcpClientConfigProvider[] CreateAll() =>
            new IMcpClientConfigProvider[]
            {
                new ClaudeCodeMcpConfigProvider(),
                new CodexMcpConfigProvider(),
                new CursorMcpConfigProvider()
            };
    }
}
