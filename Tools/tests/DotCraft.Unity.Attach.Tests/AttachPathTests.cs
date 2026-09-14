using System.Diagnostics;
using DotCraft.Unity;
using Xunit;

public sealed class AttachPathTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "attach-path-" + Guid.NewGuid().ToString("N"));
    public AttachPathTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void JunctionRootAndCompiledFilesResolveToTheirPhysicalLocation()
    {
        var physical = Path.Combine(root, "实际 cache");
        var alias = Path.Combine(root, "alias");
        Directory.CreateDirectory(physical);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{alias.Replace("'", "''")}' -Target '{physical.Replace("'", "''")}' | Out-Null");
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(15000));
        Assert.Equal(0, process.ExitCode);
        try
        {
            var attempt = new AttachAttempt(1);
            Assert.Equal(physical, AttachStorage.ResolveRoot(alias, attempt), ignoreCase: true);
            var file = Path.Combine(alias, "程序集.dll");
            File.WriteAllText(file, "test");
            var resolved = AttachStorage.ResolveFile(file, "test", attempt);
            Assert.Equal(Path.Combine(physical, "程序集.dll"), resolved, ignoreCase: true);
            Assert.True(NativeInjector.SameFile(file, resolved));
            File.WriteAllText(Path.Combine(alias, "session.bootstrap"), "{\"state\":\"dispatched\"}");
            AttachStorage.CheckStateAliases(Path.Combine(alias, "session"), Path.Combine(physical, "session"), attempt);
        }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public void ConflictingOrHiddenLegacyStateIsNeverDiscarded()
    {
        var logical = Path.Combine(root, "logical");
        var physical = Path.Combine(root, "physical");
        File.WriteAllText(logical + ".bootstrap", "{\"state\":\"dispatched\"}");
        var attempt = new AttachAttempt(1);
        Assert.Equal("UnityAttachPathStateConflict", Assert.Throws<UnityTargetException>(() =>
            AttachStorage.CheckStateAliases(logical, physical, attempt)).Code);
        File.Copy(logical + ".bootstrap", physical + ".bootstrap");
        Assert.Throws<UnityTargetException>(() => AttachStorage.CheckStateAliases(logical, physical, attempt));
        Assert.True(File.Exists(logical + ".bootstrap"));
    }
}
