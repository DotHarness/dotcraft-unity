using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class ToolGatewayMcpToolsListRequest
    {
        public ToolGatewayMcpToolsListRequest(string cursor)
        {
            Cursor = cursor;
        }

        public string Cursor { get; }
    }

    internal sealed class ToolGatewayMcpToolsCallRequest
    {
        public ToolGatewayMcpToolsCallRequest(
            string name,
            JObject arguments,
            JToken progressToken)
        {
            Name = name;
            Arguments = arguments ?? new JObject();
            ProgressToken = progressToken;
        }

        public string Name { get; }

        public JObject Arguments { get; }

        public JToken ProgressToken { get; }
    }

    internal static class ToolGatewayMcpToolRequests
    {
        public static bool TryParseToolsList(
            JToken id,
            JToken paramsToken,
            out ToolGatewayMcpToolsListRequest request,
            out ToolGatewayMcpError error)
        {
            request = null;
            error = null;

            if (paramsToken != null
                && paramsToken.Type != JTokenType.Null
                && paramsToken.Type != JTokenType.Object)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: tools/list params must be an object.");
                return false;
            }

            var @params = paramsToken as JObject;
            if (@params != null
                && @params.TryGetValue("cursor", System.StringComparison.Ordinal, out var cursor)
                && cursor.Type != JTokenType.String
                && cursor.Type != JTokenType.Null)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: cursor must be a string.");
                return false;
            }

            request = new ToolGatewayMcpToolsListRequest(@params?.Value<string>("cursor"));
            return true;
        }

        public static bool TryParseToolsCall(
            JToken id,
            JToken paramsToken,
            out ToolGatewayMcpToolsCallRequest request,
            out ToolGatewayMcpError error)
        {
            request = null;
            error = null;

            if (paramsToken is not JObject @params)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: tools/call params must be an object.");
                return false;
            }

            if (@params["name"]?.Type != JTokenType.String)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: name must be a non-empty string.");
                return false;
            }

            var name = @params.Value<string>("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                error = McpErrors.InvalidParams(id, "Invalid params: name must be a non-empty string.");
                return false;
            }

            var arguments = @params["arguments"];
            if (arguments != null && arguments.Type != JTokenType.Object)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: arguments must be an object.");
                return false;
            }

            var meta = @params["_meta"];
            if (meta != null && meta.Type != JTokenType.Object)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: _meta must be an object.");
                return false;
            }

            JToken progressToken = null;
            if (meta is JObject metaObject
                && metaObject.TryGetValue("progressToken", System.StringComparison.Ordinal, out progressToken)
                && progressToken.Type != JTokenType.String
                && progressToken.Type != JTokenType.Integer)
            {
                error = McpErrors.InvalidParams(id, "Invalid params: _meta.progressToken must be a string or integer.");
                return false;
            }

            request = new ToolGatewayMcpToolsCallRequest(
                name,
                arguments as JObject,
                progressToken);
            return true;
        }
    }
}
