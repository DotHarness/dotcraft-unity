using System.Diagnostics;
using System.Text.Json;

namespace DotCraft.Unity.McpGateway.Tests;

public sealed class ProjectStateStoreTests : IDisposable
{
    private readonly string _projectRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-unity-mcp-tests",
        Guid.NewGuid().ToString("N"));

    public ProjectStateStoreTests() => Directory.CreateDirectory(_projectRoot);

    [Fact]
    public void MissingManifestUsesStableExecuteCSharpFallback()
    {
        var manifest = new ProjectStateStore(_projectRoot).ReadManifestOrDefault();

        Assert.Equal(GatewayConstants.PackageVersion, manifest.PackageVersion);
        var tool = Assert.Single(manifest.Tools);
        Assert.Equal(GatewayConstants.ExecuteCSharpToolName, tool.Name);
        Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
    }

    [Fact]
    public void ManifestToolsAreSortedByOrdinalName()
    {
        WriteJson(GatewayConstants.ManifestRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = GatewayConstants.PackageVersion,
            revision = "sha256:test",
            tools = new[]
            {
                new { name = "z_tool", description = "z", inputSchema = new { type = "object" } },
                new { name = "a_tool", description = "a", inputSchema = new { type = "object" } }
            }
        });

        var manifest = new ProjectStateStore(_projectRoot).ReadManifestOrDefault();

        Assert.Equal(new[] { "a_tool", "z_tool" }, manifest.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void DiscoveryRequiresLiveProcessAndLoopbackToolGatewayPath()
    {
        WriteJson(GatewayConstants.DiscoveryRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = GatewayConstants.PackageVersion,
            processId = Process.GetCurrentProcess().Id,
            endpoint = "http://127.0.0.1:49152/dotcraft-unity",
            token = "token"
        });

        var discovery = new ProjectStateStore(_projectRoot).ReadLiveDiscovery();

        Assert.NotNull(discovery);
        Assert.Equal("token", discovery.Token);
    }

    [Fact]
    public void NonLoopbackDiscoveryIsRejected()
    {
        WriteJson(GatewayConstants.DiscoveryRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = GatewayConstants.PackageVersion,
            processId = Process.GetCurrentProcess().Id,
            endpoint = "http://example.com/dotcraft-unity",
            token = "token"
        });

        Assert.Null(new ProjectStateStore(_projectRoot).ReadLiveDiscovery());
    }

    [Fact]
    public void InvalidManifestAndStaleDiscoveryFallBackSafely()
    {
        WriteJson(GatewayConstants.ManifestRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = "0.0.0",
            revision = "sha256:invalid",
            tools = Array.Empty<object>()
        });
        WriteJson(GatewayConstants.DiscoveryRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = GatewayConstants.PackageVersion,
            processId = int.MaxValue,
            endpoint = "http://127.0.0.1:49152/dotcraft-unity",
            token = "token"
        });

        var store = new ProjectStateStore(_projectRoot);
        Assert.Equal(GatewayConstants.ExecuteCSharpToolName, Assert.Single(store.ReadManifestOrDefault().Tools).Name);
        Assert.Null(store.ReadLiveDiscovery());
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    private void WriteJson(string relativePath, object value)
    {
        var path = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }
}
