using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DotCraft.Unity.McpGateway;

internal sealed class UnityToolGatewayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ProjectStateStore _stateStore;
    private readonly HttpClient _httpClient;

    public UnityToolGatewayClient(ProjectStateStore stateStore, HttpClient? httpClient = null)
    {
        _stateStore = stateStore;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(65)
        };
    }

    public async Task<UnityToolGatewayResult> CallAsync(
        string name,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken,
        string? sessionId = null)
    {
        var discovery = _stateStore.ReadLiveDiscovery();
        if (discovery is null)
            return Unavailable(name, "Unity Tool Gateway is unavailable because Unity is not running or reloading.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            discovery.Endpoint.TrimEnd('/') + "/call");
        request.Headers.TryAddWithoutValidation(GatewayConstants.ToolGatewayTokenHeader, discovery.Token);
        if (!string.IsNullOrEmpty(sessionId))
            request.Headers.TryAddWithoutValidation(GatewayConstants.ToolGatewaySessionHeader, sessionId);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                name,
                arguments = arguments ?? new Dictionary<string, JsonElement>()
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || response.StatusCode == HttpStatusCode.NotFound)
            {
                return Unavailable(name, "Unity Tool Gateway discovery is stale.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Disconnected(
                    name,
                    $"Unity Tool Gateway returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var result = await response.Content
                .ReadFromJsonAsync<UnityToolGatewayResult>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? Disconnected(name, "Unity Tool Gateway returned an empty response.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return Disconnected(name, $"Unity disconnected during the tool call: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Disconnected(name, $"Unity disconnected during the tool call: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Disconnected(name, $"Unity returned an invalid tool result: {ex.Message}");
        }
    }

    /// <summary>Returns null when Unity is unreachable, which is an ordinary state.</summary>
    public async Task<ClientPresenceAck?> PostPresenceAsync(
        ClientPresenceRequest presence,
        CancellationToken cancellationToken)
    {
        var discovery = _stateStore.ReadLiveDiscovery();
        if (discovery is null)
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            discovery.Endpoint.TrimEnd('/') + "/session");
        request.Headers.TryAddWithoutValidation(GatewayConstants.ToolGatewayTokenHeader, discovery.Token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(presence, JsonOptions),
            Encoding.UTF8,
            "application/json");

        // The shared HttpClient timeout is sized for tool calls (65s).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content
                .ReadFromJsonAsync<ClientPresenceAck>(JsonOptions, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private static UnityToolGatewayResult Unavailable(string name, string message) => new()
    {
        Success = false,
        Name = name,
        ErrorCode = "UnityUnavailable",
        ErrorMessage = message,
        Text = $"{name} failed: {message}"
    };

    private static UnityToolGatewayResult Disconnected(string name, string message) => new()
    {
        Success = false,
        Name = name,
        ErrorCode = "UnityDisconnected",
        ErrorMessage = message,
        Text = $"{name} failed: {message}"
    };
}
