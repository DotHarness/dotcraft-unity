using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DotCraft.Unity.McpGateway.Tests;

internal sealed class CliFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dotcraft-unity-cli-tests", Guid.NewGuid().ToString("N"));
    public string WorkingDirectory { get; set; }
    public HttpListener Listener { get; } = new();
    public int Port { get; }
    public string Token { get; set; } = Guid.NewGuid().ToString("N");

    public CliFixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Assets", "Nested"));
        Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
        WorkingDirectory = Root;
        using (var socket = new TcpListener(IPAddress.Loopback, 0))
        {
            socket.Start();
            Port = ((IPEndPoint)socket.LocalEndpoint).Port;
        }
        Listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        Listener.Start();
        WriteDiscovery();
    }

    public void WriteDiscovery(string? version = null, string? endpoint = null, int? pid = null) =>
        WriteJson(GatewayConstants.DiscoveryRelativePath, new
        {
            schemaVersion = GatewayConstants.SchemaVersion,
            packageVersion = version ?? GatewayConstants.PackageVersion,
            processId = pid ?? Environment.ProcessId,
            endpoint = endpoint ?? $"http://127.0.0.1:{Port}/dotcraft-unity",
            token = Token
        });

    public void WriteJson(string relativePath, object data)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }

    public Task<CliOutput> RunAsync(params string[] args) => RunWithInputAsync(null, args);

    public async Task<CliOutput> RunWithInputAsync(string? input, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "dotcraft-unity.exe"),
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
            return new CliOutput(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    public async Task<JsonElement> ReplyOnceAsync(bool success = true, int status = 200)
    {
        var ct = TestContext.Current.CancellationToken;
        var context = await Listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(15), ct);
        Assert.Equal("POST", context.Request.HttpMethod);
        Assert.Equal(GatewayConstants.ToolGatewayCallPath, context.Request.Url!.AbsolutePath);
        Assert.Equal(Token, context.Request.Headers[GatewayConstants.ToolGatewayTokenHeader]);
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        using var document = JsonDocument.Parse(await reader.ReadToEndAsync(ct));
        var request = document.RootElement.Clone();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            success, name = request.GetProperty("name").GetString(),
            result = new { returnValue = new { answer = 42, label = "测试" } },
            text = success ? "answer: 42" : "compile failed",
            errorCode = success ? null : "CompilationFailed", errorMessage = success ? null : "bad C#",
            durationMs = 7
        });
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.Close();
        return request;
    }

    public void Dispose()
    {
        Listener.Close();
        Directory.Delete(Root, true);
    }
}

internal sealed record CliOutput(int ExitCode, string Stdout, string Stderr)
{
    public JsonElement Json => JsonSerializer.Deserialize<JsonElement>(Stdout);
}
