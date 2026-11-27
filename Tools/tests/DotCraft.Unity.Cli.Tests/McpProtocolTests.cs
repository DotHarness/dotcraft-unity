using ModelContextProtocol.Client;

namespace DotCraft.Unity.Cli.Tests;

public sealed class McpProtocolTests : IDisposable
{
    private readonly string _projectRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-unity-mcp-protocol-tests",
        Guid.NewGuid().ToString("N"));

    public McpProtocolTests() => Directory.CreateDirectory(_projectRoot);

    [Fact]
    public async Task OfficialSdkInitializesListsAndCallsTheAttachTools()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "dotcraft-unity.exe");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotcraft-unity-test",
            Command = executable,
            Arguments = ["mcp", "--project-root", _projectRoot]
        });
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = "2025-06-18" },
            cancellationToken: TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([UnityTools.ExecuteCSharp, UnityTools.Recompile], tools.Select(tool => tool.Name));

        var unavailable = await client.CallToolAsync(
            UnityTools.ExecuteCSharp,
            new Dictionary<string, object?> { ["code"] = "return 1;" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(unavailable.IsError);
        Assert.Equal("UnityUnavailable", unavailable.StructuredContent?.GetProperty("errorCode").GetString());
        Assert.Equal(UnityTools.ExecuteCSharp, unavailable.StructuredContent?.GetProperty("name").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }
}
