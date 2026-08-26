using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DotCraft.Unity.McpGateway.Tests;

/// <summary>Wire-level coverage of the presence heartbeat, with an HttpListener standing in for Unity.</summary>
public sealed class ClientPresenceTests : IDisposable
{
    private const string ProtocolVersion = "2025-06-18";
    private const string ClientName = "presence-test";

    private readonly string _projectRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-unity-mcp-presence-tests",
        Guid.NewGuid().ToString("N"));

    public ClientPresenceTests() => Directory.CreateDirectory(_projectRoot);

    [Fact]
    public async Task PresenceRegistersAfterInitializeWithoutAnyToolCall()
    {
        using var unity = StartFakeUnity(out var port);
        WriteDiscovery(port, "secret");
        WriteManifest("sha256:presence");

        using var process = StartGatewayProcess();
        try
        {
            await HandshakeAsync(process);

            var beat = await unity.WaitForBeatAsync(TimeSpan.FromSeconds(20));

            Assert.Equal("secret", beat.Token);
            Assert.Equal("online", beat.Body.GetProperty("state").GetString());
            Assert.False(string.IsNullOrWhiteSpace(beat.Body.GetProperty("sessionId").GetString()));
            Assert.Equal(process.Id, beat.Body.GetProperty("processId").GetInt32());
        }
        finally
        {
            await StopAsync(process);
        }
    }

    [Fact]
    public async Task PresenceReportsTheNegotiatedClientIdentity()
    {
        using var unity = StartFakeUnity(out var port);
        WriteDiscovery(port, "secret");
        WriteManifest("sha256:presence");

        using var process = StartGatewayProcess();
        try
        {
            await HandshakeAsync(process);

            var identified = await unity.WaitForBeatAsync(
                TimeSpan.FromSeconds(20),
                beat => beat.Body.TryGetProperty("client", out var client)
                        && client.ValueKind == JsonValueKind.Object);

            var client = identified.Body.GetProperty("client");
            Assert.Equal(ClientName, client.GetProperty("name").GetString());
        }
        finally
        {
            await StopAsync(process);
        }
    }

    [Fact]
    public async Task GracefulShutdownSendsClosingState()
    {
        using var unity = StartFakeUnity(out var port);
        WriteDiscovery(port, "secret");
        WriteManifest("sha256:presence");

        using var process = StartGatewayProcess();
        try
        {
            await HandshakeAsync(process);
            await unity.WaitForBeatAsync(TimeSpan.FromSeconds(20));

            // Closing stdin is how an MCP host ends a stdio server.
            process.StandardInput.Close();

            var closing = await unity.WaitForBeatAsync(
                TimeSpan.FromSeconds(20),
                beat => beat.Body.GetProperty("state").GetString() == "closing");
            Assert.Equal("closing", closing.Body.GetProperty("state").GetString());
        }
        finally
        {
            await StopAsync(process);
        }
    }

    [Fact]
    public async Task UnityUnavailableProducesNoRequestsAndKeepsTheServerAlive()
    {
        using var unity = StartFakeUnity(out _);
        // No discovery file at all: Unity is not running.
        WriteManifest("sha256:presence");

        using var process = StartGatewayProcess();
        try
        {
            await HandshakeAsync(process);

            await SendJsonRpcAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            });
            var listed = await ReadJsonRpcResponseAsync(process.StandardOutput, 2);

            Assert.True(listed.GetProperty("result").GetProperty("tools").GetArrayLength() > 0);
            Assert.False(process.HasExited);
            Assert.Null(await unity.TryWaitForBeatAsync(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            await StopAsync(process);
        }
    }

    [Fact]
    public async Task HeartbeatKeepsBeatingAndClampsAnOutOfRangeInterval()
    {
        // Unity asks for a 1s cadence; the gateway must floor it at MinHeartbeatSeconds so a bad or
        // hostile acknowledgement cannot turn the heartbeat into a tight loop.
        using var unity = StartFakeUnity(out var port, heartbeatSeconds: 1);
        WriteDiscovery(port, "secret");
        WriteManifest("sha256:presence");

        using var process = StartGatewayProcess();
        try
        {
            await HandshakeAsync(process);
            await unity.WaitForBeatAsync(TimeSpan.FromSeconds(20));

            var window = TimeSpan.FromSeconds(GatewayConstants.MinHeartbeatSeconds * 2 + 2);
            var beats = await unity.CountBeatsAsync(window);

            Assert.True(beats >= 1, $"Presence must keep beating; saw {beats} in {window.TotalSeconds}s.");
            Assert.True(
                beats <= 4,
                $"A 1s request must be clamped to {GatewayConstants.MinHeartbeatSeconds}s; saw {beats} beats "
                + $"in {window.TotalSeconds}s.");
        }
        finally
        {
            await StopAsync(process);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    // ---- Fake Unity ---------------------------------------------------------------------

    private static FakeUnity StartFakeUnity(out int port, int heartbeatSeconds = 1)
    {
        port = GetAvailablePort();
        return new FakeUnity(port, heartbeatSeconds);
    }

    private sealed record PresenceBeat(JsonElement Body, string? Token);

    private sealed class FakeUnity : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<PresenceBeat> _beats = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cancellation = new();

        public FakeUnity(int port, int heartbeatSeconds)
        {
            HeartbeatSeconds = heartbeatSeconds;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public int HeartbeatSeconds { get; }

        public async Task<PresenceBeat> WaitForBeatAsync(
            TimeSpan timeout,
            Func<PresenceBeat, bool>? predicate = null)
        {
            var beat = await TryWaitForBeatAsync(timeout, predicate);
            Assert.NotNull(beat);
            return beat!;
        }

        public async Task<PresenceBeat?> TryWaitForBeatAsync(
            TimeSpan timeout,
            Func<PresenceBeat, bool>? predicate = null)
        {
            using var deadline = new CancellationTokenSource(timeout);
            try
            {
                while (true)
                {
                    await _signal.WaitAsync(deadline.Token);
                    if (!_beats.TryDequeue(out var beat))
                        continue;
                    if (predicate is null || predicate(beat))
                        return beat;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<int> CountBeatsAsync(TimeSpan window)
        {
            while (_beats.TryDequeue(out _))
            {
                // Drain anything already buffered so the count covers only this window.
            }

            await Task.Delay(window);

            var count = 0;
            while (_beats.TryDequeue(out _))
                count++;
            return count;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                try
                {
                    await HandleAsync(context);
                }
                catch
                {
                    // The fake Unity must never take the test process down.
                }
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            var isPresence = context.Request.Url?.AbsolutePath.EndsWith("/session", StringComparison.Ordinal) == true;
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var raw = await reader.ReadToEndAsync();

            if (isPresence)
            {
                using var document = JsonDocument.Parse(raw);
                _beats.Enqueue(new PresenceBeat(
                    document.RootElement.Clone(),
                    context.Request.Headers[GatewayConstants.ToolGatewayTokenHeader]));
                _signal.Release();

                await WriteJsonAsync(context, new { success = true, heartbeatSeconds = HeartbeatSeconds });
                return;
            }

            await WriteJsonAsync(context, new
            {
                success = true,
                name = GatewayConstants.ExecuteCSharpToolName,
                result = new { success = true, returnValue = "ok" },
                text = "ok",
                durationMs = 1
            });
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, object body)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // Already torn down.
            }

            _cancellation.Dispose();
            _signal.Dispose();
        }
    }

    // ---- Gateway process ----------------------------------------------------------------

    private Process StartGatewayProcess()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "dotcraft-unity-mcp.exe");
        Assert.True(File.Exists(executable), $"Gateway executable not found: {executable}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--project-root");
        startInfo.ArgumentList.Add(_projectRoot);
        return Process.Start(startInfo)!;
    }

    private static async Task HandshakeAsync(Process process)
    {
        await SendJsonRpcAsync(process.StandardInput, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new { name = ClientName, version = "1.0" }
            }
        });
        await ReadJsonRpcResponseAsync(process.StandardOutput, 1);
        await SendJsonRpcAsync(process.StandardInput, new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.StandardInput.Close();
            if (!process.WaitForExit(3000))
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch
        {
            // Already gone.
        }
    }

    private static async Task SendJsonRpcAsync(StreamWriter writer, object message)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(message));
        await writer.FlushAsync();
    }

    private static async Task<JsonElement> ReadJsonRpcResponseAsync(StreamReader reader, int id)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotNull(line);
            using var document = JsonDocument.Parse(line!);
            if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                return document.RootElement.Clone();
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void WriteManifest(string revision)
    {
        WriteJson(GatewayConstants.ManifestRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = GatewayConstants.PackageVersion,
            revision,
            tools = new[]
            {
                new
                {
                    name = GatewayConstants.ExecuteCSharpToolName,
                    description = "Presence test tool.",
                    inputSchema = new { type = "object", additionalProperties = false }
                }
            }
        });
    }

    private void WriteDiscovery(int port, string token)
    {
        WriteJson(GatewayConstants.DiscoveryRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = GatewayConstants.PackageVersion,
            processId = Process.GetCurrentProcess().Id,
            endpoint = $"http://127.0.0.1:{port}/dotcraft-unity",
            token
        });
    }

    private void WriteJson(string relativePath, object value)
    {
        var path = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }
}
