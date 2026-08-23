using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Unity.McpGateway.Tests;

public sealed class McpProtocolTests : IDisposable
{
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
            new McpClientOptions { ProtocolVersion = "2025-11-25" });
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

    private void WriteJson(string relativePath, object value)
    {
        var path = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }
}
