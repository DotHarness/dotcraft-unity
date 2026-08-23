using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Unity.McpGateway.Tests;

public sealed class McpProtocolTests : IDisposable
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly string _projectRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-unity-mcp-protocol-tests",
        Guid.NewGuid().ToString("N"));

    public McpProtocolTests() => Directory.CreateDirectory(_projectRoot);

    [Fact]
    public async Task OfficialSdkInitializesListsCallsAndReceivesListChanged()
    {
        await using var client = await CreateClientAsync();

        var initialTools = await client.ListToolsAsync();
        var execute = Assert.Single(initialTools);
        Assert.Equal(GatewayConstants.ExecuteCSharpToolName, execute.Name);

        var unavailable = await client.CallToolAsync(
            execute.Name,
            new Dictionary<string, object?> { ["code"] = "return 1;" });
        Assert.True(unavailable.IsError);
        Assert.Contains("UnityUnavailable", unavailable.StructuredContent?.GetRawText());
        Assert.Equal(
            GatewayConstants.ExecuteCSharpToolName,
            unavailable.StructuredContent?.GetProperty("name").GetString());

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) =>
            {
                changed.TrySetResult();
                return ValueTask.CompletedTask;
            });

        WriteManifest("sha256:changed", "example_ping");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var changedTools = await client.ListToolsAsync();
        Assert.Equal("example_ping", Assert.Single(changedTools).Name);
    }

    [Fact]
    public async Task WireRequestForwardsToolNameAndArguments()
    {
        var port = GetAvailablePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        WriteManifest("sha256:wire", GatewayConstants.ExecuteCSharpToolName);
        WriteDiscovery(port, "secret");

        using var process = StartGatewayProcess();
        try
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
                    clientInfo = new { name = "gateway-wire-test", version = "1.0" }
                }
            });
            var initialized = await ReadJsonRpcResponseAsync(process.StandardOutput, 1);
            Assert.Equal(
                ProtocolVersion,
                initialized.GetProperty("result").GetProperty("protocolVersion").GetString());

            await SendJsonRpcAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await SendJsonRpcAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            });
            var listed = await ReadJsonRpcResponseAsync(process.StandardOutput, 2);
            var tool = Assert.Single(listed.GetProperty("result").GetProperty("tools").EnumerateArray());
            Assert.Equal(GatewayConstants.ExecuteCSharpToolName, tool.GetProperty("name").GetString());

            var gatewayRequest = CaptureGatewayCallAsync(listener);
            await SendJsonRpcAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = GatewayConstants.ExecuteCSharpToolName,
                    arguments = new { code = "return 42;", mode = "editor" }
                }
            });
            var call = await ReadJsonRpcResponseAsync(process.StandardOutput, 3);
            var forwarded = await gatewayRequest.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(call.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Equal(GatewayConstants.ExecuteCSharpToolName, forwarded.GetProperty("name").GetString());
            Assert.Equal("return 42;", forwarded.GetProperty("arguments").GetProperty("code").GetString());
            Assert.Equal("editor", forwarded.GetProperty("arguments").GetProperty("mode").GetString());
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task OfficialSdkCancellationCancelsAnInFlightToolGatewayCall()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        WriteDiscovery(port, "secret");

        await using var client = await CreateClientAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var acceptTask = listener.AcceptTcpClientAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CallToolAsync(
            GatewayConstants.ExecuteCSharpToolName,
            new Dictionary<string, object?> { ["code"] = "return 1;" },
            cancellationToken: cancellation.Token).AsTask());

        using var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    private async Task<McpClient> CreateClientAsync()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "dotcraft-unity-mcp.exe");
        Assert.True(File.Exists(executable), $"Gateway executable not found: {executable}");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotcraft-unity-test",
            Command = executable,
            Arguments = ["--project-root", _projectRoot],
            WorkingDirectory = _projectRoot
        });
        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = ProtocolVersion });
    }

    private Process StartGatewayProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetGatewayExecutable(),
            WorkingDirectory = _projectRoot,
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

    private static async Task SendJsonRpcAsync(StreamWriter writer, object message)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(message));
        await writer.FlushAsync();
    }

    private static async Task<JsonElement> ReadJsonRpcResponseAsync(StreamReader reader, int id)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(line);
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                return document.RootElement.Clone();
        }
    }

    private static string GetGatewayExecutable()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "dotcraft-unity-mcp.exe");
        Assert.True(File.Exists(executable), $"Gateway executable not found: {executable}");
        return executable;
    }

    private void WriteManifest(string revision, string toolName)
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
                    name = toolName,
                    description = "Protocol test tool.",
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

    private static async Task<JsonElement> CaptureGatewayCallAsync(HttpListener listener)
    {
        var context = await listener.GetContextAsync();
        Assert.True(context.Request.ContentLength64 > 0);
        Assert.Null(context.Request.Headers["Transfer-Encoding"]);
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        using var document = JsonDocument.Parse(await reader.ReadToEndAsync());
        var request = document.RootElement.Clone();
        var responseBody = JsonSerializer.Serialize(new
        {
            success = true,
            name = GatewayConstants.ExecuteCSharpToolName,
            result = new { success = true, returnValue = "ok" },
            text = "ok",
            durationMs = 1
        });
        var responseBytes = Encoding.UTF8.GetBytes(responseBody);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes);
        context.Response.Close();
        return request;
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void WriteJson(string relativePath, object value)
    {
        var path = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }
}
