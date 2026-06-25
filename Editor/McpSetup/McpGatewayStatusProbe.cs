using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotCraft.Editor.McpSetup
{
    internal sealed class McpGatewayStatusProbe
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        private readonly Func<string, string, string, CancellationToken, Task<McpGatewayProbeHttpResponse>> _sendAsync;

        public McpGatewayStatusProbe(
            Func<string, string, string, CancellationToken, Task<McpGatewayProbeHttpResponse>> sendAsync = null)
        {
            _sendAsync = sendAsync ?? SendHttpAsync;
        }

        public async Task<McpGatewayProbeResult> ProbeAsync(string endpoint, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return McpGatewayProbeResult.Failed("Unavailable", "MCP endpoint is empty.");

            if (!McpGatewaySetupDefaults.IsLoopbackEndpoint(endpoint))
                return McpGatewayProbeResult.Failed("Invalid endpoint", "MCP endpoint must be a loopback HTTP URL.");

            var initialize = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""dotcraft-unity-setup"",""version"":""1""}}}";
            var initializeResponse = await SendJsonRpcAsync(endpoint, initialize, ct).ConfigureAwait(false);
            if (!initializeResponse.Success)
                return initializeResponse;

            var tools = @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list"",""params"":{}}";
            var toolsResponse = await SendJsonRpcAsync(endpoint, tools, ct).ConfigureAwait(false);
            if (!toolsResponse.Success)
                return toolsResponse;

            try
            {
                var root = JObject.Parse(toolsResponse.Body);
                var toolArray = root["result"]?["tools"] as JArray;
                if (toolArray == null)
                    return McpGatewayProbeResult.Failed("Invalid response", "MCP tools/list response did not include result.tools.");

                var names = toolArray
                    .Select(tool => tool?["name"]?.Value<string>())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();

                return new McpGatewayProbeResult(
                    true,
                    names.Length == 0 ? "Connected, no tools exposed" : $"Connected, {names.Length} tool(s)",
                    string.Empty,
                    names);
            }
            catch (JsonException ex)
            {
                return McpGatewayProbeResult.Failed("Invalid response", $"Invalid JSON from tools/list: {ex.Message}");
            }
        }

        private async Task<McpGatewayProbeRawResult> SendJsonRpcAsync(
            string endpoint,
            string body,
            CancellationToken ct)
        {
            McpGatewayProbeHttpResponse response;
            try
            {
                response = await _sendAsync("POST", endpoint, body, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return McpGatewayProbeRawResult.Failed("Stopped", ex.Message);
            }

            if (response.Status < 200 || response.Status >= 300)
                return McpGatewayProbeRawResult.Failed("HTTP error", $"HTTP {response.Status}: {response.Body}");

            try
            {
                var root = JObject.Parse(response.Body);
                if (root["error"] != null && root["error"]?.Type != JTokenType.Null)
                    return McpGatewayProbeRawResult.Failed("MCP error", root["error"]?["message"]?.Value<string>() ?? "Unknown MCP error.");

                return McpGatewayProbeRawResult.Ok(response.Body);
            }
            catch (JsonException ex)
            {
                return McpGatewayProbeRawResult.Failed("Invalid response", $"Invalid JSON: {ex.Message}");
            }
        }

        private static Task<McpGatewayProbeHttpResponse> SendHttpAsync(
            string method,
            string endpoint,
            string body,
            CancellationToken ct)
        {
            return SendHttpCoreAsync(method, endpoint, body, ct);
        }

        private static async Task<McpGatewayProbeHttpResponse> SendHttpCoreAsync(
            string method,
            string endpoint,
            string body,
            CancellationToken ct)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), endpoint)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json")
            };
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new McpGatewayProbeHttpResponse((int)response.StatusCode, responseBody);
        }

        private sealed class McpGatewayProbeRawResult
        {
            private McpGatewayProbeRawResult(bool success, string body, string status, string error)
            {
                Success = success;
                Body = body ?? string.Empty;
                Status = status ?? string.Empty;
                Error = error ?? string.Empty;
            }

            public bool Success { get; }

            public string Body { get; }

            public string Status { get; }

            public string Error { get; }

            public static McpGatewayProbeRawResult Ok(string body) =>
                new(true, body, string.Empty, string.Empty);

            public static McpGatewayProbeRawResult Failed(string status, string error) =>
                new(false, string.Empty, status, error);

            public static implicit operator McpGatewayProbeResult(McpGatewayProbeRawResult result) =>
                McpGatewayProbeResult.Failed(result.Status, result.Error);
        }
    }
}
