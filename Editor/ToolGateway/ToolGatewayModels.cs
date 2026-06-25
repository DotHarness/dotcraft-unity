using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayToolSpec
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public JObject InputSchema { get; set; }
    }

    internal sealed class ToolGatewayResult
    {
        public bool Success { get; set; }

        public string Name { get; set; }

        public object StructuredResult { get; set; }

        public string Text { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public long DurationMs { get; set; }

        public static ToolGatewayResult Ok(string name, object structuredResult, string text, long durationMs)
        {
            return new ToolGatewayResult
            {
                Success = true,
                Name = name,
                StructuredResult = structuredResult,
                Text = text,
                DurationMs = durationMs
            };
        }

        public static ToolGatewayResult Failed(
            string name,
            string errorCode,
            string errorMessage,
            long durationMs,
            object structuredResult = null)
        {
            return new ToolGatewayResult
            {
                Success = false,
                Name = name,
                StructuredResult = structuredResult,
                Text = string.IsNullOrWhiteSpace(errorMessage)
                    ? $"{name} failed."
                    : $"{name} failed: {errorMessage}",
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                DurationMs = durationMs
            };
        }
    }

    internal sealed class ToolGatewayHttpResponse
    {
        public int Status { get; set; }

        public string Reason { get; set; }

        public string ContentType { get; set; }

        public string Body { get; set; }

        public static ToolGatewayHttpResponse Json(object body, int status = 200, string reason = "OK")
        {
            return new ToolGatewayHttpResponse
            {
                Status = status,
                Reason = reason,
                ContentType = "application/json; charset=utf-8",
                Body = DotCraft.Editor.Protocol.DotCraftJson.Serialize(body)
            };
        }

        public static ToolGatewayHttpResponse Text(
            string body,
            string contentType,
            int status = 200,
            string reason = "OK")
        {
            return new ToolGatewayHttpResponse
            {
                Status = status,
                Reason = reason,
                ContentType = contentType,
                Body = body ?? string.Empty
            };
        }

        public static ToolGatewayHttpResponse Accepted()
        {
            return new ToolGatewayHttpResponse
            {
                Status = 202,
                Reason = "Accepted",
                ContentType = "text/plain; charset=utf-8",
                Body = string.Empty
            };
        }

        public static ToolGatewayHttpResponse Error(int status, string reason, string message)
        {
            return Json(new
            {
                success = false,
                errorCode = reason.Replace(" ", string.Empty),
                errorMessage = message
            }, status, reason);
        }
    }
}
