using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayJsonRpcMessage
    {
        public ToolGatewayJsonRpcMessage(JObject @object, bool hasId, JToken id, string method)
        {
            Object = @object;
            HasId = hasId;
            Id = id;
            Method = method;
        }

        public JObject Object { get; }

        public bool HasId { get; }

        public JToken Id { get; }

        public string Method { get; }

        public JToken Params => Object["params"];

        public bool IsRequest => HasId && !string.IsNullOrWhiteSpace(Method);

        public bool IsNotification => !HasId && !string.IsNullOrWhiteSpace(Method);

        public bool IsResponse => HasId && string.IsNullOrWhiteSpace(Method);
    }

    internal static class ToolGatewayJsonRpcEnvelope
    {
        public static bool TryParse(
            string body,
            out ToolGatewayJsonRpcMessage message,
            out ToolGatewayMcpError error)
        {
            message = null;
            error = null;

            JToken root;
            try
            {
                if (string.IsNullOrWhiteSpace(body))
                    throw new FormatException("Request body is empty.");

                root = JToken.Parse(body);
            }
            catch (Exception ex)
            {
                error = McpErrors.ParseError($"Parse error: {ex.Message}");
                return false;
            }

            if (root.Type != JTokenType.Object)
            {
                error = McpErrors.InvalidRequest(
                    null,
                    "Invalid Request: MCP endpoint accepts exactly one JSON-RPC object.",
                    400,
                    "Bad Request");
                return false;
            }

            var request = (JObject)root;
            if (!request.Properties().Any())
            {
                error = McpErrors.InvalidRequest(
                    null,
                    "Invalid Request: empty JSON-RPC object.",
                    400,
                    "Bad Request");
                return false;
            }

            if (request["jsonrpc"]?.Type != JTokenType.String
                || !string.Equals(request.Value<string>("jsonrpc"), "2.0", StringComparison.Ordinal))
            {
                error = McpErrors.InvalidRequest(
                    null,
                    "Invalid Request: jsonrpc must be \"2.0\".",
                    400,
                    "Bad Request");
                return false;
            }

            var hasId = request.Property("id") != null;
            var id = request["id"];
            if (hasId && !IsValidId(id))
            {
                error = McpErrors.InvalidRequest(
                    null,
                    "Invalid Request: id must be a string or integer.",
                    400,
                    "Bad Request");
                return false;
            }

            var methodToken = request["method"];
            var hasMethod = methodToken != null;
            if (hasMethod
                && (methodToken.Type != JTokenType.String || string.IsNullOrWhiteSpace(methodToken.Value<string>())))
            {
                error = McpErrors.InvalidRequest(
                    id,
                    "Invalid Request: method must be a non-empty string.",
                    400,
                    "Bad Request");
                return false;
            }

            if (!hasMethod)
            {
                var hasResult = request.Property("result") != null;
                var hasError = request.Property("error") != null;
                if (!hasId || hasResult == hasError)
                {
                    error = McpErrors.InvalidRequest(
                        id,
                        "Invalid Request: missing method.",
                        400,
                        "Bad Request");
                    return false;
                }
            }

            message = new ToolGatewayJsonRpcMessage(
                request,
                hasId,
                id,
                hasMethod ? methodToken.Value<string>() : null);
            return true;
        }

        public static bool IsValidId(JToken id)
        {
            return id != null
                   && (id.Type == JTokenType.String || id.Type == JTokenType.Integer);
        }

        public static string ToRequestKey(JToken id)
        {
            return id.Type == JTokenType.String
                ? "s:" + id.Value<string>()
                : "i:" + id.Value<long>().ToString(CultureInfo.InvariantCulture);
        }
    }
}
