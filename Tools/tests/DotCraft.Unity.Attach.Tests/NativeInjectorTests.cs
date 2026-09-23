using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using DotCraft.Unity;
using Xunit;

public sealed class NativeInjectorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "attach-native-" + Guid.NewGuid().ToString("N"));
    public NativeInjectorTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void DiagnosticsAreNdjsonWithoutExceptionMessageContents()
    {
        var path = Path.Combine(root, "diagnostics.ndjson");
        var attempt = new AttachAttempt(123, diagnosticsPath: path);
        attempt.Step("preflight", new { nativePath = "test.dll" });
        try { throw new InvalidOperationException("secret-token-and-snippet-must-not-be-logged"); }
        catch (Exception error) { attempt.Failed(error); }
        var rows = File.ReadAllLines(path).Select(line => JsonNode.Parse(line)!).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("preflight", rows[1]["stage"]!.GetValue<string>());
        Assert.Equal(rows[0]["operationId"]!.GetValue<string>(), rows[1]["operationId"]!.GetValue<string>());
        Assert.NotNull(rows[1]["details"]!["stack"]);
        Assert.DoesNotContain("secret-token", File.ReadAllText(path));
    }

    [Fact]
    public void ThreadCreationFailureLeavesMemoryReleasableAndJournalRetryable()
    {
        var journal = Path.Combine(root, "create.json");
        var attempt = new AttachAttempt(1, journal);
        bool canFree = true;
        Assert.Equal("UnityAttachCreateRemoteThreadFailed", Assert.Throws<UnityTargetException>(
            () => NativeInjector.RunThread(0, 0, 0, attempt, "load-library", 1, ref canFree)).Code);
        Assert.True(canFree);
        Assert.True(AttachAttempt.CanRetry(JsonNode.Parse(File.ReadAllText(journal))));
    }

    [Fact]
    public void CompletedBootstrapLeavesJournalRetryableAfterHandshakeTimeout()
    {
        var journal = Path.Combine(root, "bootstrapped.json");
        var attempt = new AttachAttempt(1, journal);
        attempt.Dispatch("bootstrap");
        Assert.False(AttachAttempt.CanRetry(JsonNode.Parse(File.ReadAllText(journal))));

        attempt.Bootstrapped();
        Assert.True(AttachAttempt.CanRetry(JsonNode.Parse(File.ReadAllText(journal))));

        attempt.Failed(new UnityTargetException("UnityAttachHandshakeTimeout", "Handshake timed out."));
        Assert.True(AttachAttempt.CanRetry(JsonNode.Parse(File.ReadAllText(journal))));
    }

    [Fact]
    public void RemoteLoaderReturnsFullModuleHandleAndTargetError()
    {
        using var child = StartChild();
        var attempt = new AttachAttempt(child.Id, Path.Combine(root, "load.json"));
        var handle = NativeInjector.OpenTarget(child.Id, attempt);
        try
        {
            // A real DLL absent from the waiting PowerShell process before this call.
            var native = AttachStorage.ExtractNative(root);
            var module = NativeInjector.LoadRemoteLibrary(child, handle, native, attempt);
            child.Refresh();
            var loaded = NativeInjector.Modules(child).Single(m => NativeInjector.SameFile(m.Path, native));
            Assert.Equal(loaded.Base, module);
            Assert.True((ulong)module > uint.MaxValue); // x64 HMODULE must not be truncated to DWORD.

            var missing = new AttachAttempt(child.Id, Path.Combine(root, "missing.json"));
            var error = Assert.Throws<UnityTargetException>(() => NativeInjector.LoadRemoteLibrary(child, handle,
                Path.Combine(root, "does-not-exist.dll"), missing));
            Assert.Equal("UnityAttachLoadLibraryFailed", error.Code);
            Assert.Contains("Win32 126", error.Message);
            Assert.True(missing.RetrySafe);
            Assert.True(AttachAttempt.CanRetry(JsonNode.Parse(File.ReadAllText(Path.Combine(root, "missing.json")))));
        }
        finally { NativeInjector.CloseHandle(handle); child.Kill(true); child.WaitForExit(); }
    }

    [Fact]
    public void TimedOutThreadRetainsMemoryAndBlocksReplay()
    {
        using var child = StartChild();
        var journal = Path.Combine(root, "timeout.json");
        var attempt = new AttachAttempt(child.Id, journal);
        var handle = NativeInjector.OpenTarget(child.Id, attempt);
        var kernel = NativeLibrary.Load("kernel32.dll");
        try
        {
            using var self = Process.GetCurrentProcess();
            var sleep = NativeInjector.ResolveExport(NativeLibrary.GetExport(kernel, "Sleep"),
                NativeInjector.Modules(self), NativeInjector.Modules(child), attempt);
            bool canFree = true;
            var error = Assert.Throws<UnityTargetException>(() => NativeInjector.RunThread(handle, sleep,
                (nint)5000, attempt, "test-sleep", 1, ref canFree));
            Assert.Equal("UnityAttachRemoteTimeout", error.Code);
            Assert.False(canFree);
            Assert.False(attempt.RetrySafe);
            attempt.Failed(error);
            Assert.False(AttachAttempt.CanRetry(JsonNode.Parse(File.ReadAllText(journal))));
        }
        finally { NativeLibrary.Free(kernel); NativeInjector.CloseHandle(handle); child.Kill(true); child.WaitForExit(); }
    }

    private static Process StartChild()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell/v1.0/powershell.exe")) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[Console]::WriteLine('ready'); Start-Sleep -Seconds 60");
        var process = Process.Start(start)!;
        try
        {
            Assert.Equal("ready", process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult());
            return process;
        }
        catch { process.Kill(true); process.Dispose(); throw; }
    }
}
