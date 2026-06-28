using System;
using System.Linq;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayMcpProtocol
    {
        public const string ProtocolVersion = "2025-11-25";
        public const string ServerVersion = "0.2.1";
        public const string ServerName = "dotcraft-unity";
        public const string ServerTitle = "dotcraft-unity Tool Gateway";
        public const string Instructions =
            "Use unity_execute_csharp to inspect or modify the running Unity Editor. The gateway also exposes enabled custom project tools registered with dotcraft-unity.";

        public static readonly string[] SupportedProtocolVersions = { ProtocolVersion };

        public static bool IsSupportedProtocolVersion(string version)
        {
            return SupportedProtocolVersions.Contains(version, StringComparer.Ordinal);
        }

        internal static class Paths
        {
            public const string Mcp = "/dotcraft/mcp";
            public const string GatewayTools = "/dotcraft/gateway/tools";
            public const string GatewayCall = "/dotcraft/gateway/call";
        }

        internal static class Headers
        {
            public const string Accept = "Accept";
            public const string Allow = "Allow";
            public const string McpSessionId = "MCP-Session-Id";
            public const string McpProtocolVersion = "MCP-Protocol-Version";
        }

        internal static class HttpMethods
        {
            public const string Get = "GET";
            public const string Post = "POST";
            public const string Delete = "DELETE";
        }

        internal static class AllowValues
        {
            public const string PostDelete = "POST, DELETE";
        }

        internal static class MediaTypes
        {
            public const string Json = "application/json";
            public const string EventStream = "text/event-stream";
        }

        internal static class Methods
        {
            public const string Initialize = "initialize";
            public const string Ping = "ping";
            public const string ToolsList = "tools/list";
            public const string ToolsCall = "tools/call";
            public const string LoggingSetLevel = "logging/setLevel";
        }

        internal static class Notifications
        {
            public const string Prefix = "notifications/";
            public const string Initialized = "notifications/initialized";
            public const string Cancelled = "notifications/cancelled";
        }
    }

    internal static class McpJsonRpcErrorCodes
    {
        /// <summary>JSON-RPC 2.0 parse error: invalid JSON was received by the server.</summary>
        public const int ParseError = -32700;

        /// <summary>JSON-RPC 2.0 invalid request: the JSON is not a valid request object.</summary>
        public const int InvalidRequest = -32600;

        /// <summary>JSON-RPC 2.0 method not found: the requested method does not exist or is unavailable.</summary>
        public const int MethodNotFound = -32601;

        /// <summary>JSON-RPC 2.0 invalid params: method parameters are invalid for the requested method.</summary>
        public const int InvalidParams = -32602;
    }

    internal static class McpProtocolErrorCodes
    {
        /// <summary>dotcraft-unity MCP lifecycle error: the MCP session header is missing or no session matches it.</summary>
        public const int MissingSession = -32001;

        /// <summary>dotcraft-unity MCP lifecycle error: a valid session has not completed notifications/initialized.</summary>
        public const int SessionNotInitialized = -32002;

        /// <summary>MCP Streamable HTTP transport error: the MCP-Protocol-Version header is unsupported.</summary>
        public const int UnsupportedProtocolVersion = -32022;
    }

    internal sealed class ToolGatewayMcpError
    {
        public ToolGatewayMcpError(
            JToken id,
            int code,
            string message,
            int httpStatus = 200,
            string httpReason = "OK",
            object data = null)
        {
            Id = id;
            Code = code;
            Message = message;
            HttpStatus = httpStatus;
            HttpReason = httpReason;
            Data = data;
        }

        public JToken Id { get; }

        public int Code { get; }

        public string Message { get; }

        public int HttpStatus { get; }

        public string HttpReason { get; }

        public object Data { get; }
    }

    internal static class McpErrors
    {
        public static ToolGatewayMcpError ParseError(string message)
        {
            return new ToolGatewayMcpError(
                null,
                McpJsonRpcErrorCodes.ParseError,
                message,
                400,
                "Bad Request");
        }

        public static ToolGatewayMcpError InvalidRequest(
            JToken id,
            string message,
            int httpStatus = 200,
            string httpReason = "OK")
        {
            return new ToolGatewayMcpError(
                id,
                McpJsonRpcErrorCodes.InvalidRequest,
                message,
                httpStatus,
                httpReason);
        }

        public static ToolGatewayMcpError MethodNotFound(JToken id, string methodName)
        {
            return new ToolGatewayMcpError(
                id,
                McpJsonRpcErrorCodes.MethodNotFound,
                $"Method not found: {methodName}");
        }

        public static ToolGatewayMcpError InvalidParams(JToken id, string message)
        {
            return new ToolGatewayMcpError(
                id,
                McpJsonRpcErrorCodes.InvalidParams,
                message);
        }

        public static ToolGatewayMcpError MissingSessionHeader()
        {
            return new ToolGatewayMcpError(
                null,
                McpProtocolErrorCodes.MissingSession,
                "Missing MCP-Session-Id.",
                400,
                "Bad Request");
        }

        public static ToolGatewayMcpError UnknownSession(JToken id = null)
        {
            return new ToolGatewayMcpError(
                id,
                McpProtocolErrorCodes.MissingSession,
                "Unknown MCP session.",
                404,
                "Not Found");
        }

        public static ToolGatewayMcpError SessionNotInitialized(JToken id)
        {
            return new ToolGatewayMcpError(
                id,
                McpProtocolErrorCodes.SessionNotInitialized,
                "MCP session is not initialized.");
        }

        public static ToolGatewayMcpError UnsupportedProtocolVersion(string requestedVersion)
        {
            return new ToolGatewayMcpError(
                null,
                McpProtocolErrorCodes.UnsupportedProtocolVersion,
                "Unsupported MCP-Protocol-Version.",
                400,
                "Bad Request",
                new
                {
                    supported = ToolGatewayMcpProtocol.SupportedProtocolVersions,
                    requested = requestedVersion
                });
        }
    }

    internal static class ToolGatewayMcpResponses
    {
        public static ToolGatewayHttpResponse JsonRpcResult(JToken id, object result)
        {
            return ToolGatewayHttpResponse.Json(new JsonRpcResponse
            {
                Id = id,
                Result = result
            });
        }

        public static ToolGatewayHttpResponse JsonRpcError(ToolGatewayMcpError error)
        {
            return ToolGatewayHttpResponse.Json(new JsonRpcResponse
            {
                Id = error.Id,
                Error = new JsonRpcError
                {
                    Code = error.Code,
                    Message = error.Message,
                    Data = error.Data
                }
            }, error.HttpStatus, error.HttpReason);
        }

        public static ToolGatewayHttpResponse Accepted()
        {
            return ToolGatewayHttpResponse.Accepted();
        }

        public static ToolGatewayHttpResponse NoContent()
        {
            return ToolGatewayHttpResponse.NoBody(204, "No Content");
        }
    }
}
