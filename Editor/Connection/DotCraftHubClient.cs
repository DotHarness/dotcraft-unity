using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Settings;
using Debug = UnityEngine.Debug;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// Lightweight client for DotCraft Hub discovery and AppServer ensure.
    /// This client only talks to the Hub HTTP API; Unity still communicates with DotCraft through ACP.
    /// </summary>
    public sealed class DotCraftHubClient
    {
        private const int StartupTimeoutMs = 15000;
        private const int PollIntervalMs = 200;
        private static readonly HttpClient Http = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Ensures a Hub-managed AppServer exists for the workspace and returns its WebSocket endpoint.
        /// </summary>
        public async Task<string> EnsureAppServerWebSocketAsync(
            string dotCraftCommand,
            string workspacePath,
            IReadOnlyDictionary<string, string> environmentVariables,
            CancellationToken ct)
        {
            var hub = await EnsureHubAsync(dotCraftCommand, workspacePath, environmentVariables, ct);
            var response = await SendEnsureRequestAsync(hub, workspacePath, ct);
            if (response.Endpoints == null
                || !response.Endpoints.TryGetValue("appServerWebSocket", out var endpoint)
                || string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Hub did not return an AppServer WebSocket endpoint.");
            }

            return endpoint.Trim();
        }

        private static async Task<HubLockInfo> EnsureHubAsync(
            string dotCraftCommand,
            string workspacePath,
            IReadOnlyDictionary<string, string> environmentVariables,
            CancellationToken ct)
        {
            var live = await TryGetLiveHubAsync(ct);
            if (live != null)
                return live;

            StartHub(dotCraftCommand, workspacePath, environmentVariables);

            var deadline = DateTime.UtcNow.AddMilliseconds(StartupTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                live = await TryGetLiveHubAsync(ct);
                if (live != null)
                    return live;
                await Task.Delay(PollIntervalMs, ct);
            }

            throw new InvalidOperationException("DotCraft Hub could not be started.");
        }

        private static async Task<HubLockInfo> TryGetLiveHubAsync(CancellationToken ct)
        {
            var info = ReadHubLock();
            if (info == null || !IsProcessAlive(info.Pid))
                return null;

            try
            {
                using var response = await Http.GetAsync($"{info.ApiBaseUrl}/v1/status", ct);
                return response.IsSuccessStatusCode ? info : null;
            }
            catch
            {
                return null;
            }
        }

        private static HubLockInfo ReadHubLock()
        {
            var lockPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "hub",
                "hub.lock");
            if (!File.Exists(lockPath))
                return null;

            try
            {
                var json = File.ReadAllText(lockPath);
                var info = JsonSerializer.Deserialize<HubLockInfo>(json, JsonOptions);
                if (info == null
                    || info.Pid <= 0
                    || string.IsNullOrWhiteSpace(info.ApiBaseUrl)
                    || string.IsNullOrWhiteSpace(info.Token))
                {
                    return null;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void StartHub(
            string dotCraftCommand,
            string workspacePath,
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            var startInfo = DotCraftProcessManager.CreateProcessStartInfo(
                dotCraftCommand,
                "hub",
                workspacePath,
                environmentVariables,
                redirectStreams: false);
            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start DotCraft Hub process.");

            if (DotCraftSettings.Instance.VerboseLogging)
                Debug.Log($"[DotCraft] Hub process started (PID: {process.Id})");
        }

        private static async Task<HubAppServerResponse> SendEnsureRequestAsync(
            HubLockInfo hub,
            string workspacePath,
            CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                workspacePath,
                client = new
                {
                    name = "dotcraft-unity",
                    version = "0.1.0"
                },
                startIfMissing = true
            }, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{hub.ApiBaseUrl}/v1/appservers/ensure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hub.Token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw CreateHubException(response, text);

            return JsonSerializer.Deserialize<HubAppServerResponse>(text, JsonOptions)
                   ?? throw new InvalidOperationException("Hub returned an empty AppServer response.");
        }

        private static Exception CreateHubException(HttpResponseMessage response, string body)
        {
            try
            {
                var error = JsonSerializer.Deserialize<HubErrorResponse>(body, JsonOptions);
                if (error?.Error != null
                    && (!string.IsNullOrWhiteSpace(error.Error.Code) || !string.IsNullOrWhiteSpace(error.Error.Message)))
                {
                    return new InvalidOperationException(
                        $"Hub {error.Error.Code ?? "requestFailed"}: {error.Error.Message ?? response.ReasonPhrase}");
                }
            }
            catch
            {
                // Fall through to generic message.
            }

            return new InvalidOperationException($"Hub request failed with HTTP {(int)response.StatusCode}.");
        }

        private sealed class HubLockInfo
        {
            public int Pid { get; set; }
            public string ApiBaseUrl { get; set; }
            public string Token { get; set; }
        }

        private sealed class HubAppServerResponse
        {
            public Dictionary<string, string> Endpoints { get; set; }
        }

        private sealed class HubErrorResponse
        {
            public HubError Error { get; set; }
        }

        private sealed class HubError
        {
            public string Code { get; set; }
            public string Message { get; set; }
        }
    }
}
