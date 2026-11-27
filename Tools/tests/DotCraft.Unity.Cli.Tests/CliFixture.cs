using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotCraft.Unity.Cli.Tests;

internal sealed class CliFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dotcraft-unity-cli-tests", Guid.NewGuid().ToString("N"));
    public string WorkingDirectory { get; set; }

    public CliFixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Assets", "Nested"));
        Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
        WorkingDirectory = Root;
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

    public void Dispose() => Directory.Delete(Root, true);
}

internal sealed record CliOutput(int ExitCode, string Stdout, string Stderr)
{
    public JsonElement Json => JsonSerializer.Deserialize<JsonElement>(Stdout);
}
