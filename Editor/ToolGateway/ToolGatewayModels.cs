using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayHttpRequestContext
    {
        public string Method { get; set; }

        public string Target { get; set; }

        public IReadOnlyDictionary<string, string> Headers { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Body { get; set; } = string.Empty;

        public string GetHeader(string name)
        {
            return Headers.TryGetValue(name, out var value) ? value : null;
        }
    }

    internal sealed class UnityToolSpec
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public JObject InputSchema { get; set; }
    }

    internal sealed class UnityToolResult
    {
        public bool Success { get; set; }

        public string Name { get; set; }

        public object StructuredResult { get; set; }

        public string Text { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public long DurationMs { get; set; }

        public static UnityToolResult Ok(string name, object structuredResult, string text, long durationMs)
        {
            return new UnityToolResult
            {
                Success = true,
                Name = name,
                StructuredResult = structuredResult,
                Text = text,
                DurationMs = durationMs
            };
        }

        public static UnityToolResult Failed(
            string name,
            string errorCode,
            string errorMessage,
            long durationMs,
            object structuredResult = null)
        {
            return new UnityToolResult
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

        public static ToolGatewayHttpResponse Error(int status, string reason, string errorCode, string message)
        {
            return Json(new
            {
                success = false,
                errorCode,
                errorMessage = message
            }, status, reason);
        }
    }
}
