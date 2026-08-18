using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayHttpHandler
    {
        public static bool CanHandle(string target)
        {
            var path = GetPath(target);
            return string.Equals(path, ToolGatewayMcpProtocol.Paths.Mcp, StringComparison.Ordinal)
                   || string.Equals(path, ToolGatewayMcpProtocol.Paths.GatewayTools, StringComparison.Ordinal)
                   || string.Equals(path, ToolGatewayMcpProtocol.Paths.GatewayCall, StringComparison.Ordinal);
        }

        public static async Task<ToolGatewayHttpResponse> HandleAsync(
            string method,
            string target,
            string body,
            CancellationToken ct)
        {
            return await HandleAsync(new ToolGatewayHttpRequestContext
            {
                Method = method,
                Target = target,
                Body = body
            }, ct).ConfigureAwait(false);
        }

        public static async Task<ToolGatewayHttpResponse> HandleAsync(
            ToolGatewayHttpRequestContext request,
            CancellationToken ct)
        {
            var path = GetPath(request.Target);
            if (string.Equals(path, ToolGatewayMcpProtocol.Paths.Mcp, StringComparison.Ordinal))
                return await HandleMcpAsync(request, ct).ConfigureAwait(false);

            if (string.Equals(path, ToolGatewayMcpProtocol.Paths.GatewayTools, StringComparison.Ordinal))
                return HandleToolsAsync(request.Method, request.Target);

            if (string.Equals(path, ToolGatewayMcpProtocol.Paths.GatewayCall, StringComparison.Ordinal))
                return await HandleGatewayCallAsync(request.Method, request.Body, ct).ConfigureAwait(false);

            return ToolGatewayHttpResponse.Error(404, "Not Found", "Unknown DotCraft Unity Tool Gateway path.");
        }

        private static async Task<ToolGatewayHttpResponse> HandleMcpAsync(
            ToolGatewayHttpRequestContext context,
            CancellationToken ct)
        {
            if (!ValidateProtocolVersionHeader(context, out var versionError))
                return ToolGatewayMcpResponses.JsonRpcError(versionError);

            if (string.Equals(context.Method, ToolGatewayMcpProtocol.HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
                return HandleMcpGet();

            if (string.Equals(context.Method, ToolGatewayMcpProtocol.HttpMethods.Delete, StringComparison.OrdinalIgnoreCase))
                return HandleMcpDelete(context);

            if (!string.Equals(context.Method, ToolGatewayMcpProtocol.HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
                return ToolGatewayHttpResponse.Error(405, "Method Not Allowed", "MCP endpoint supports POST, GET, and DELETE.");

            if (!AcceptsRequiredMcpPostTypes(context))
            {
                return ToolGatewayMcpResponses.JsonRpcError(McpErrors.InvalidRequest(
                    null,
                    "Invalid Request: MCP POST requests must include Accept with application/json and text/event-stream.",
                    406,
                    "Not Acceptable"));
            }

            if (!ToolGatewayJsonRpcEnvelope.TryParse(context.Body, out var message, out var parseError))
                return ToolGatewayMcpResponses.JsonRpcError(parseError);

            if (message.IsResponse)
            {
                return TryGetSession(context, out _, out var responseSessionError)
                    ? ToolGatewayMcpResponses.Accepted()
                    : responseSessionError;
            }

            if (string.Equals(message.Method, ToolGatewayMcpProtocol.Methods.Initialize, StringComparison.Ordinal))
            {
                if (!message.IsRequest)
                {
                    return ToolGatewayMcpResponses.JsonRpcError(McpErrors.InvalidRequest(
                        null,
                        "Invalid Request: initialize must be a JSON-RPC request."));
                }

                var existingSessionId = context.GetHeader(ToolGatewayMcpProtocol.Headers.McpSessionId);
                if (!string.IsNullOrWhiteSpace(existingSessionId))
                {
                    if (ToolGatewayMcpSessionStore.Contains(existingSessionId))
                    {
                        return ToolGatewayMcpResponses.JsonRpcError(McpErrors.InvalidRequest(
                            message.Id,
                            "Invalid Request: initialize must not be sent with an existing MCP-Session-Id.",
                            400,
                            "Bad Request"));
                    }

                    return ToolGatewayMcpResponses.JsonRpcError(McpErrors.UnknownSession(message.Id));
                }

                var negotiatedVersion = NegotiateProtocolVersion(message.Params as JObject);
                var session = ToolGatewayMcpSessionStore.Create(negotiatedVersion);
                return ToolGatewayMcpResponses.JsonRpcResult(message.Id, BuildInitializeResult(negotiatedVersion))
                    .WithHeader(ToolGatewayMcpProtocol.Headers.McpSessionId, session.Id);
            }

            if (!TryGetSession(context, out var mcpSession, out var errorResponse))
                return errorResponse;

            if (message.Method.StartsWith(ToolGatewayMcpProtocol.Notifications.Prefix, StringComparison.Ordinal)
                && message.HasId)
            {
                return ToolGatewayMcpResponses.JsonRpcError(McpErrors.InvalidRequest(
                    message.Id,
                    "Invalid Request: JSON-RPC notifications must not include id.",
                    400,
                    "Bad Request"));
            }

            if (message.IsNotification)
            {
                if (string.Equals(message.Method, ToolGatewayMcpProtocol.Notifications.Initialized, StringComparison.Ordinal))
                {
                    ToolGatewayMcpSessionStore.MarkInitialized(mcpSession.Id, out _);
                    return ToolGatewayMcpResponses.Accepted();
                }

                if (string.Equals(message.Method, ToolGatewayMcpProtocol.Notifications.Cancelled, StringComparison.Ordinal))
                    return HandleMcpCancellationNotification(mcpSession, message.Params);

                return ToolGatewayMcpResponses.Accepted();
            }

            if (!mcpSession.Initialized && !string.Equals(message.Method, ToolGatewayMcpProtocol.Methods.Ping, StringComparison.Ordinal))
            {
                return ToolGatewayMcpResponses.JsonRpcError(McpErrors.SessionNotInitialized(message.Id));
            }

            switch (message.Method)
            {
                case ToolGatewayMcpProtocol.Methods.Ping:
                    return ToolGatewayMcpResponses.JsonRpcResult(message.Id, new { });
                case ToolGatewayMcpProtocol.Methods.ToolsList:
                    return HandleMcpToolsList(message.Id, message.Params);
                case ToolGatewayMcpProtocol.Methods.ToolsCall:
                    return await HandleMcpToolCallAsync(
                        mcpSession,
                        message.Id,
                        message.Params,
                        ct).ConfigureAwait(false);
                default:
                    return ToolGatewayMcpResponses.JsonRpcError(McpErrors.MethodNotFound(message.Id, message.Method));
            }
        }

        private static ToolGatewayHttpResponse HandleMcpGet()
        {
            return ToolGatewayHttpResponse.Error(
                405,
                "Method Not Allowed",
                "MCP endpoint does not offer a server-sent event stream.")
                .WithHeader(
                    ToolGatewayMcpProtocol.Headers.Allow,
                    ToolGatewayMcpProtocol.AllowValues.PostDelete);
        }

        private static ToolGatewayHttpResponse HandleMcpDelete(ToolGatewayHttpRequestContext context)
        {
            if (!TryGetSession(context, out var session, out var errorResponse))
                return errorResponse;

            ToolGatewayMcpSessionStore.Remove(session.Id);
            return ToolGatewayMcpResponses.NoContent();
        }

        private static ToolGatewayHttpResponse HandleToolsAsync(string method, string target)
        {
            if (!string.Equals(method, ToolGatewayMcpProtocol.HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
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
            if (!string.Equals(method, ToolGatewayMcpProtocol.HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
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

        private static ToolGatewayHttpResponse HandleMcpToolsList(JToken id, JToken paramsToken)
        {
            if (!ToolGatewayMcpToolRequests.TryParseToolsList(
                    id,
                    paramsToken,
                    out _,
                    out var error))
                return ToolGatewayMcpResponses.JsonRpcError(error);

            return ToolGatewayMcpResponses.JsonRpcResult(id, new
            {
                tools = UnityToolGateway.Instance.ListTools()
                    .Select(ToolGatewayAdapters.ProjectMcpTool)
                    .ToArray()
            });
        }

        private static ToolGatewayHttpResponse HandleMcpCancellationNotification(
            ToolGatewayMcpSession session,
            JToken paramsToken)
        {
            var @params = paramsToken as JObject;
            var requestId = @params?["requestId"];
            if (!ToolGatewayJsonRpcEnvelope.IsValidId(requestId))
                return ToolGatewayMcpResponses.Accepted();

            ToolGatewayMcpSessionStore.CancelRequest(
                session.Id,
                ToolGatewayJsonRpcEnvelope.ToRequestKey(requestId));
            return ToolGatewayMcpResponses.Accepted();
        }

        private static async Task<ToolGatewayHttpResponse> HandleMcpToolCallAsync(
            ToolGatewayMcpSession session,
            JToken id,
            JToken paramsToken,
            CancellationToken ct)
        {
            if (!ToolGatewayMcpToolRequests.TryParseToolsCall(
                    id,
                    paramsToken,
                    out var request,
                    out var error))
                return ToolGatewayMcpResponses.JsonRpcError(error);

            if (!UnityToolGateway.Instance.HasTool(request.Name))
            {
                return ToolGatewayMcpResponses.JsonRpcError(McpErrors.InvalidParams(
                    id,
                    $"Invalid params: unknown or disabled tool '{request.Name}'."));
            }

            var requestKey = ToolGatewayJsonRpcEnvelope.ToRequestKey(id);
            if (!ToolGatewayMcpSessionStore.TryTrackRequest(
                    session.Id,
                    requestKey,
                    ct,
                    out var tracker))
            {
                return ToolGatewayMcpResponses.JsonRpcError(McpErrors.UnknownSession(id));
            }

            try
            {
                var result = await UnityToolGateway.Instance
                    .CallAsync(request.Name, request.Arguments, tracker.Token)
                    .ConfigureAwait(false);
                return ToolGatewayMcpResponses.JsonRpcResult(id, ToolGatewayAdapters.ProjectMcpToolResult(result));
            }
            catch (OperationCanceledException) when (tracker.CancelledByClient)
            {
                return ToolGatewayMcpResponses.Accepted();
            }
            finally
            {
                ToolGatewayMcpSessionStore.CompleteRequest(session.Id, requestKey, tracker);
            }
        }

        private static object BuildInitializeResult(string negotiatedVersion)
        {
            return new
            {
                protocolVersion = negotiatedVersion,
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = false
                    }
                },
                serverInfo = new
                {
                    name = ToolGatewayMcpProtocol.ServerName,
                    title = ToolGatewayMcpProtocol.ServerTitle,
                    version = ToolGatewayMcpProtocol.ServerVersion
                },
                instructions = ToolGatewayMcpProtocol.Instructions
            };
        }

        private static string NegotiateProtocolVersion(JObject @params)
        {
            var requestedVersion = @params?.Value<string>("protocolVersion");
            return string.Equals(requestedVersion, ToolGatewayMcpProtocol.ProtocolVersion, StringComparison.Ordinal)
                ? requestedVersion
                : ToolGatewayMcpProtocol.ProtocolVersion;
        }

        private static bool TryGetSession(
            ToolGatewayHttpRequestContext context,
            out ToolGatewayMcpSession session,
            out ToolGatewayHttpResponse errorResponse)
        {
            session = null;
            errorResponse = null;

            var sessionId = context.GetHeader(ToolGatewayMcpProtocol.Headers.McpSessionId);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                errorResponse = ToolGatewayMcpResponses.JsonRpcError(McpErrors.MissingSessionHeader());
                return false;
            }

            if (!ToolGatewayMcpSessionStore.TryGet(sessionId, out session))
            {
                errorResponse = ToolGatewayMcpResponses.JsonRpcError(McpErrors.UnknownSession());
                return false;
            }

            return true;
        }

        private static bool ValidateProtocolVersionHeader(
            ToolGatewayHttpRequestContext context,
            out ToolGatewayMcpError error)
        {
            error = null;
            var requestedVersion = context.GetHeader(ToolGatewayMcpProtocol.Headers.McpProtocolVersion);
            if (string.IsNullOrWhiteSpace(requestedVersion)
                || ToolGatewayMcpProtocol.IsSupportedProtocolVersion(requestedVersion))
            {
                return true;
            }

            error = McpErrors.UnsupportedProtocolVersion(requestedVersion);
            return false;
        }

        private static bool AcceptsRequiredMcpPostTypes(ToolGatewayHttpRequestContext context)
        {
            var accept = context.GetHeader(ToolGatewayMcpProtocol.Headers.Accept);
            return AcceptHeaderContains(accept, ToolGatewayMcpProtocol.MediaTypes.Json)
                   && AcceptHeaderContains(accept, ToolGatewayMcpProtocol.MediaTypes.EventStream);
        }

        private static bool AcceptHeaderContains(string accept, string mediaType)
        {
            if (string.IsNullOrWhiteSpace(accept))
                return false;

            return accept
                .Split(',')
                .Select(value => value.Split(';')[0].Trim())
                .Any(value => string.Equals(value, mediaType, StringComparison.OrdinalIgnoreCase));
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
