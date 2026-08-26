using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Extensions;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class UnityToolGatewayHandler
    {
        public const string BasePath = "/dotcraft-unity";
        public const string SessionPath = "/session";

        private readonly string _token;

        public UnityToolGatewayHandler(string token)
        {
            _token = token;
        }

        public async Task<ToolGatewayHttpResponse> HandleAsync(
            ToolGatewayHttpRequestContext context,
            CancellationToken cancellationToken)
        {
            if (!TokensMatch(context.GetHeader(UnityToolGatewayState.TokenHeader), _token))
            {
                return ToolGatewayHttpResponse.Error(
                    401,
                    "Unauthorized",
                    "InvalidGatewayToken",
                    "A valid DotCraft Unity MCP Gateway token is required.");
            }

            var path = GetPath(context.Target);
            if (string.Equals(path, BasePath + "/call", StringComparison.Ordinal))
                return await HandleCallAsync(context, cancellationToken).ConfigureAwait(false);

            if (string.Equals(path, BasePath + SessionPath, StringComparison.Ordinal))
                return HandleSession(context);

            return ToolGatewayHttpResponse.Error(404, "Not Found", "RouteNotFound", "Unity Tool Gateway route was not found.");
        }

        /// <summary>Stays off the main thread so presence works while Unity is compiling.</summary>
        private static ToolGatewayHttpResponse HandleSession(ToolGatewayHttpRequestContext context)
        {
            if (!string.Equals(context.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return MethodNotAllowed("Unity Tool Gateway client presence supports POST.");

            JObject request;
            try
            {
                request = string.IsNullOrWhiteSpace(context.Body) ? new JObject() : JObject.Parse(context.Body);
            }
            catch (Exception ex)
            {
                return ToolGatewayHttpResponse.Error(400, "Bad Request", "InvalidJson", $"Invalid JSON: {ex.Message}");
            }

            if (!McpClientSessionRegistry.TryParse(request, out var session, out var isClosing))
            {
                return ToolGatewayHttpResponse.Error(
                    400,
                    "Bad Request",
                    "InvalidSession",
                    "A client presence session id is required.");
            }

            var registry = McpClientSessionRegistry.Instance;
            var changed = isClosing
                ? registry.Remove(session.SessionId)
                : registry.Upsert(session, DateTime.UtcNow);
            if (changed)
                UnityToolGatewayRuntime.Instance.NotifySessionsChanged();

            return ToolGatewayHttpResponse.Json(new
            {
                success = true,
                heartbeatSeconds = (int)McpClientSessionRegistry.HeartbeatInterval.TotalSeconds
            });
        }

        private static async Task<ToolGatewayHttpResponse> HandleCallAsync(
            ToolGatewayHttpRequestContext context,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(context.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return MethodNotAllowed("Unity Tool Gateway call supports POST.");

            JObject request;
            try
            {
                request = string.IsNullOrWhiteSpace(context.Body) ? new JObject() : JObject.Parse(context.Body);
            }
            catch (Exception ex)
            {
                return ToolGatewayHttpResponse.Error(400, "Bad Request", "InvalidJson", $"Invalid JSON: {ex.Message}");
            }

            var name = request.Value<string>("name");
            var arguments = request["arguments"] ?? new JObject();

            McpClientSessionRegistry.Instance.Touch(
                context.GetHeader(UnityToolGatewayState.SessionHeader),
                DateTime.UtcNow);

            var result = await MainThreadDispatcher
                .RunOnMainThread(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return UnityToolRegistry.Instance.CallAsync(name, arguments, cancellationToken);
                    },
                    timeoutMs: 65000)
                .ConfigureAwait(false);
            return ToolGatewayHttpResponse.Json(ToolGatewayAdapters.ProjectGatewayResult(result));
        }

        private static ToolGatewayHttpResponse MethodNotAllowed(string message) =>
            ToolGatewayHttpResponse.Error(405, "Method Not Allowed", "MethodNotAllowed", message);

        private static string GetPath(string target)
        {
            if (!Uri.TryCreate("http://127.0.0.1" + target, UriKind.Absolute, out var uri))
                return string.Empty;
            return uri.AbsolutePath;
        }

        private static bool TokensMatch(string provided, string expected)
        {
            if (provided == null || expected == null)
                return false;

            var providedBytes = Encoding.UTF8.GetBytes(provided);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            if (providedBytes.Length != expectedBytes.Length)
                return false;

            var difference = 0;
            for (var index = 0; index < providedBytes.Length; index++)
                difference |= providedBytes[index] ^ expectedBytes[index];
            return difference == 0;
        }
    }
}
