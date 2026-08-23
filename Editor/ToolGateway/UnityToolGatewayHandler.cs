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
                return await HandleCallAsync(context.Method, context.Body, cancellationToken).ConfigureAwait(false);

            return ToolGatewayHttpResponse.Error(404, "Not Found", "RouteNotFound", "Unity Tool Gateway route was not found.");
        }

        private static async Task<ToolGatewayHttpResponse> HandleCallAsync(
            string method,
            string body,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                return MethodNotAllowed("Unity Tool Gateway call supports POST.");

            JObject request;
            try
            {
                request = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return ToolGatewayHttpResponse.Error(400, "Bad Request", "InvalidJson", $"Invalid JSON: {ex.Message}");
            }

            var name = request.Value<string>("name");
            var arguments = request["arguments"] ?? new JObject();
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
