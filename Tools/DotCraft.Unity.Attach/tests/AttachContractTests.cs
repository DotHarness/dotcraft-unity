using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Unity;
using Xunit;

public sealed class AttachContractTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "attach-tests-" + Guid.NewGuid().ToString("N"));

    public AttachContractTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task ReadOnlyDiscoveryNeedsNoNativeResourceOrCacheWrites()
    {
        await using var client = new UnityAttachService(root, Path.Combine(root, "missing.dll"));
        Assert.NotNull(client.List());
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public async Task ReusedPidIsRejectedBeforeNetworkDispatch()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var path = WriteConnection(listener);
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        record["startUtc"] = DateTime.UtcNow.AddDays(-1);
        AttachStorage.Write(path, record);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BridgeClient.Call(
            path, "execute_start", cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(listener.Pending());
    }

    [Fact]
    public void DomainGenerationChangesCompiledSnippetIdentity()
    {
        var source = Path.Combine(root, "snippet.cs");
        File.WriteAllText(source, "public class Snippet { public static int Run() { return 42; } }");
        var references = new[] { typeof(object).Assembly.Location };
        var first = TargetCompiler.Compile(source, references, root, "Snippet_", "old-domain");
        var second = TargetCompiler.Compile(source, references, root, "Snippet_", "new-domain");
        Assert.NotEqual(first, second);
        Assert.Equal(first, TargetCompiler.Compile(source, references, root, "Snippet_", "old-domain"));
    }

    [Fact]
    public async Task UnknownTargetDoesNotBootstrap()
    {
        await using var client = new UnityAttachService(root, Path.Combine(root, "missing.dll"));
        var error = await Assert.ThrowsAsync<UnityTargetException>(() => client.Connect(
            "task", null, TestContext.Current.CancellationToken));
        Assert.Equal("UnityTargetRequired", error.Code);
        Assert.Empty(Directory.EnumerateFiles(root, "*.bootstrap", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExpiredOrUnknownExecutionIsLostWithoutDispatch()
    {
        await using var client = new UnityAttachService(root, "unused");
        var result = await client.Wait("task", "expired-id", 0, false, TestContext.Current.CancellationToken);
        Assert.Equal("lost", result["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentAttachLockHonorsCancellation()
    {
        var path = Path.Combine(root, "target");
        using var first = await AttachStorage.Lock(path, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachStorage.Lock(path, cancellation.Token));
        first.Dispose();
        using var next = await AttachStorage.Lock(path, CancellationToken.None);
    }

    [Fact]
    public async Task ChangedDomainIsRejectedBeforeDispatch()
    {
        var connection = Path.Combine(root, "connection.json");
        File.WriteAllText(connection, "{\"protocol\":1,\"generation\":\"new\"}");
        await Assert.ThrowsAsync<InvalidDataException>(() => BridgeClient.Call(
            connection, "execute_start", expectedGeneration: "old",
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResponseFromDifferentGenerationIsRejected()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var path = WriteConnection(listener);
        var server = Respond(listener, "{\"state\":\"completed\",\"generation\":\"different\"}");
        await Assert.ThrowsAsync<InvalidDataException>(() => BridgeClient.Call(
            path, "metadata", cancellationToken: TestContext.Current.CancellationToken));
        var request = await server;
        Assert.Equal("secret", request["token"]!.GetValue<string>());
        Assert.Equal("current", request["generation"]!.GetValue<string>());
    }

    [Fact]
    public async Task DroppedExecutionConnectionIsNotReplayed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var path = WriteConnection(listener);
        var server = Respond(listener, null);
        await Assert.ThrowsAnyAsync<IOException>(() => BridgeClient.Call(
            path, "execute_start", assembly: "snippet.dll",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("execute_start", (await server)["command"]!.GetValue<string>());
        Assert.False(listener.Pending());
    }

    private string WriteConnection(TcpListener listener)
    {
        var path = Path.Combine(root, "connection.json");
        AttachStorage.Write(path, new JsonObject
        {
            ["protocol"] = 1, ["generation"] = "current", ["token"] = "secret",
            ["pid"] = Environment.ProcessId, ["startUtc"] = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            ["port"] = ((IPEndPoint)listener.LocalEndpoint).Port, ["project"] = root
        });
        return path;
    }

    private static async Task<JsonObject> Respond(TcpListener listener, string? response)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var header = new byte[4];
        await stream.ReadExactlyAsync(header);
        var data = new byte[BitConverter.ToInt32(header)];
        await stream.ReadExactlyAsync(data);
        if (response != null)
        {
            var bytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(BitConverter.GetBytes(bytes.Length));
            await stream.WriteAsync(bytes);
        }
        return JsonNode.Parse(data)!.AsObject();
    }
}
