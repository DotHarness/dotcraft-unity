namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayAdapters
    {
        public static object ProjectGatewayResult(UnityToolResult result)
        {
            return new
            {
                success = result.Success,
                name = result.Name,
                result = result.StructuredResult,
                text = result.Text,
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage,
                durationMs = result.DurationMs
            };
        }
    }
}
