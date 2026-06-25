namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpGatewayProbeHttpResponse
    {
        public McpGatewayProbeHttpResponse(int status, string body)
        {
            Status = status;
            Body = body ?? string.Empty;
        }

        public int Status { get; }

        public string Body { get; }
    }
}
