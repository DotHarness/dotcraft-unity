namespace DotCraft.Editor.ToolGateway
{
    internal sealed class McpGatewayStatusSummary
    {
        private McpGatewayStatusSummary(bool isRunning, string endpoint, string lastError)
        {
            IsRunning = isRunning;
            Endpoint = endpoint ?? string.Empty;
            LastError = lastError ?? string.Empty;
            IsVisible = isRunning;
            Tooltip = isRunning
                ? $"DotCraft MCP Tool Gateway running. MCP endpoint: {Endpoint}. Click for status and actions."
                : string.Empty;
        }

        public static McpGatewayStatusSummary Empty { get; } =
            new(false, string.Empty, string.Empty);

        public bool IsVisible { get; }

        public bool IsRunning { get; }

        public string Endpoint { get; }

        public string LastError { get; }

        public string Tooltip { get; }

        public static McpGatewayStatusSummary FromState(bool isRunning, string endpoint, string lastError)
        {
            return new McpGatewayStatusSummary(isRunning, endpoint, lastError);
        }
    }
}
