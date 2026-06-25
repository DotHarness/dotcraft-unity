using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayHttpHandler
    {
        private const string McpPath = "/dotcraft/mcp";
        private const string GatewayToolsPath = "/dotcraft/gateway/tools";
        private const string GatewayCallPath = "/dotcraft/gateway/call";
        private const string ProtocolVersion = "2025-11-25";

        public static bool CanHandle(string target)
        {
            var path = GetPath(target);
            return string.Equals(path, McpPath, StringComparison.Ordinal)
                   || string.Equals(path, GatewayToolsPath, StringComparison.Ordinal)
                   || string.Equals(path, GatewayCallPath, StringComparison.Ordinal);
        }

        public static async Task<ToolGatewayHttpResponse> HandleAsync(
            string method,
            string target,
            string body,
            CancellationToken ct)
        {
            var path = GetPath(target);
            if (string.Equals(path, McpPath, StringComparison.Ordinal))
                return await HandleMcpAsync(method, body, ct).ConfigureAwait(false);

            if (string.Equals(path, GatewayToolsPath, StringComparison.Ordinal))
                return HandleToolsAsync(method, target);

            if (string.Equals(path, GatewayCallPath, StringComparison.Ordinal))
                return await HandleGatewayCallAsync(method, body, ct).ConfigureAwait(false);

            return ToolGatewayHttpResponse.Error(404, "Not Found", "Unknown DotCraft Unity Tool Gateway path.");
        }

        private static async Task<ToolGatewayHttpResponse> HandleMcpAsync(
            string method,
            string body,
            CancellationToken ct)
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                return ToolGatewayHttpResponse.Text(
                    ": dotcraft-unity MCP endpoint currently uses request-response HTTP\n\n",
                    "text/event-stream; charset=utf-8");
            }

            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                return ToolGatewayHttpResponse.Error(405, "Method Not Allowed", "MCP endpoint supports GET and POST.");

            JObject request;
            try
            {
                request = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return JsonRpcError(null, -32700, $"Parse error: {ex.Message}");
            }

            var id = request["id"];
            var methodName = request.Value<string>("method");
            if (string.IsNullOrWhiteSpace(methodName))
                return JsonRpcError(id, -32600, "Invalid Request: missing method.");

            if (id == null || id.Type == JTokenType.Null)
            {
                return string.Equals(methodName, "notifications/initialized", StringComparison.Ordinal)
                    ? ToolGatewayHttpResponse.Accepted()
                    : ToolGatewayHttpResponse.Accepted();
            }

            switch (methodName)
            {
                case "initialize":
                    return JsonRpcResult(id, BuildInitializeResult(request["params"] as JObject));
                case "ping":
                    return JsonRpcResult(id, new { });
                case "tools/list":
                    return JsonRpcResult(id, new
                    {
                        tools = UnityToolGateway.Instance.ListTools()
                            .Select(ToolGatewayAdapters.ProjectMcpTool)
                            .ToArray()
                    });
                case "tools/call":
                    return await HandleMcpToolCallAsync(id, request["params"] as JObject, ct).ConfigureAwait(false);
                default:
                    return JsonRpcError(id, -32601, $"Method not found: {methodName}");
            }
        }

        private static ToolGatewayHttpResponse HandleToolsAsync(string method, string target)
        {
            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                return ToolGatewayHttpResponse.Error(405, "Method Not Allowed", "Tool projection endpoint supports GET.");

            try
            {
                var query = ParseQuery(target);
                query.TryGetValue("format", out var format);
                return ToolGatewayHttpResponse.Json(ToolGatewayAdapters.ProjectTools(format));
            }
            catch (Exception ex)
            {
                return ToolGatewayHttpResponse.Error(400, "Bad Request", ex.Message);
            }
        }

        private static async Task<ToolGatewayHttpResponse> HandleGatewayCallAsync(
            string method,
            string body,
            CancellationToken ct)
        {
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                return ToolGatewayHttpResponse.Error(405, "Method Not Allowed", "Gateway call endpoint supports POST.");

            JObject request;
            try
            {
                request = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return ToolGatewayHttpResponse.Error(400, "Bad Request", $"Invalid JSON: {ex.Message}");
            }

            var namespaceName = request.Value<string>("namespace");
            if (!string.IsNullOrWhiteSpace(namespaceName)
                && !string.Equals(namespaceName, "unity", StringComparison.Ordinal))
            {
                return ToolGatewayHttpResponse.Json(ToolGatewayAdapters.ProjectGatewayResult(
                    ToolGatewayResult.Failed(
                        request.Value<string>("tool") ?? request.Value<string>("name") ?? string.Empty,
                        "NamespaceMismatch",
                        "dotcraft-unity Tool Gateway only exposes the unity namespace.",
                        0)));
            }

            var name = request.Value<string>("tool") ?? request.Value<string>("name");
            var result = await UnityToolGateway.Instance
                .CallAsync(name, request["arguments"] ?? new JObject(), ct)
                .ConfigureAwait(false);
            return ToolGatewayHttpResponse.Json(ToolGatewayAdapters.ProjectGatewayResult(result));
        }

        private static async Task<ToolGatewayHttpResponse> HandleMcpToolCallAsync(
            JToken id,
            JObject @params,
            CancellationToken ct)
        {
            if (@params == null)
                return JsonRpcError(id, -32602, "Invalid params: expected object.");

            var name = @params.Value<string>("name");
            if (string.IsNullOrWhiteSpace(name))
                return JsonRpcError(id, -32602, "Invalid params: missing tool name.");

            var result = await UnityToolGateway.Instance
                .CallAsync(name, @params["arguments"] ?? new JObject(), ct)
                .ConfigureAwait(false);
            return JsonRpcResult(id, ToolGatewayAdapters.ProjectMcpToolResult(result));
        }

        private static object BuildInitializeResult(JObject @params)
        {
            var requestedVersion = @params?.Value<string>("protocolVersion");
            var version = string.IsNullOrWhiteSpace(requestedVersion)
                ? ProtocolVersion
                : requestedVersion;

            return new
            {
                protocolVersion = version,
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = false
                    }
                },
                serverInfo = new
                {
                    name = "dotcraft-unity",
                    title = "dotcraft-unity Tool Gateway",
                    version = "0.1.6"
                },
                instructions = "Use unity_execute_csharp to inspect or modify the running Unity Editor. The gateway also exposes enabled custom project tools registered with dotcraft-unity."
            };
        }

        private static ToolGatewayHttpResponse JsonRpcResult(JToken id, object result)
        {
            return ToolGatewayHttpResponse.Json(new JsonRpcResponse
            {
                Id = id,
                Result = result
            });
        }

        private static ToolGatewayHttpResponse JsonRpcError(JToken id, int code, string message)
        {
            return ToolGatewayHttpResponse.Json(new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = message
                }
            });
        }

        private static string GetPath(string target)
        {
            var uri = new Uri("http://127.0.0.1" + target, UriKind.Absolute);
            return uri.AbsolutePath;
        }

        private static Dictionary<string, string> ParseQuery(string target)
        {
            var uri = new Uri("http://127.0.0.1" + target, UriKind.Absolute);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var query = uri.Query?.TrimStart('?') ?? string.Empty;
            if (string.IsNullOrEmpty(query))
                return result;

            foreach (var pair in query.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                    continue;

                var index = pair.IndexOf('=');
                var key = index < 0 ? pair : pair.Substring(0, index);
                var value = index < 0 ? string.Empty : pair.Substring(index + 1);
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", "%20"));
            }

            return result;
        }
    }
}
