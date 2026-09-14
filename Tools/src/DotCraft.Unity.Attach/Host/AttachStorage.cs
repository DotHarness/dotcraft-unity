using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DotCraft.Unity;

internal static class AttachStorage
{
    private static string NormalizeFinalPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length > 6 && path[5] == ':') return path[4..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new UnityTargetException("UnityAttachPathResolutionFailed", "The resolved device path cannot be passed to Unity Mono.");
        return path;
    }

    internal static string ResolveFile(string path, string purpose, AttachAttempt? attempt = null)
    {
        try
        {
            var logicalPath = Path.GetFullPath(path);
            using var file = File.OpenHandle(logicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new StringBuilder(512);
            var length = GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 0);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length >= buffer.Capacity)
            {
                buffer = new StringBuilder(checked((int)length + 1));
                length = GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 0);
                if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (length >= buffer.Capacity) throw new IOException("Final path changed while resolving the file.");
            }
            var physicalPath = NormalizeFinalPath(buffer.ToString());
            attempt?.Log("path-resolution", new { purpose, logicalPath, physicalPath, length = physicalPath.Length,
                redirected = !string.Equals(logicalPath, physicalPath, StringComparison.OrdinalIgnoreCase) });
            return physicalPath;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException)
        {
            attempt?.Log("path-resolution-failed", new { purpose, path, errorType = error.GetType().Name,
                win32Error = (error as Win32Exception)?.NativeErrorCode, error.HResult, stack = error.StackTrace });
            throw new UnityTargetException("UnityAttachPathResolutionFailed", $"Cannot resolve {purpose}: {path}. {error.Message}");
        }
    }

    internal static string ResolveRoot(string root, AttachAttempt attempt)
    {
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, ".physical-root");
        using (new FileStream(marker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) { }
        return Path.GetDirectoryName(ResolveFile(marker, "cache-root", attempt))!;
    }

    internal static void ValidateMonoPath(string path, string purpose, int suffixLength = 0)
    {
        if (path.Length + suffixLength >= 260)
            throw new UnityTargetException("UnityAttachPathTooLong", $"{purpose} actual path requires {path.Length + suffixLength} characters; Unity Mono requires fewer than 260: {path}");
    }

    internal static void CheckStateAliases(string logical, string physical, AttachAttempt attempt)
    {
        foreach (var suffix in new[] { "", ".bootstrap", ".lifecycle", ".error" })
        {
            var first = logical + suffix;
            var second = physical + suffix;
            var a = File.Exists(first);
            var b = File.Exists(second);
            if (a) ResolveFile(first, "existing-state" + suffix, attempt);
            // An unaliased legacy record must never disappear when adopting a new root.
            if ((a && !b) || (a && b && !NativeInjector.SameFile(first, second)))
                throw new UnityTargetException("UnityAttachPathStateConflict", $"Logical and physical Attach state differ: {first}; {second}. No bootstrap was sent.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    internal static string ProductVersion => typeof(UnityAttachService).Assembly.GetName().Version!.ToString(3);

    internal static void WriteSource(string path, string source)
    {
        if (File.Exists(path)) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, source);
        try { File.Move(temporary, path, false); }
        catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
    }

    internal static string RuntimeIdentity(string nativePath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(ProductVersion));
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(AttachAttempt.BuildId));
        hash.AppendData(File.ReadAllBytes(nativePath));
        var assembly = typeof(UnityAttachService).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".cs", StringComparison.Ordinal)).Order())
        {
            using var resource = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            hash.AppendData(memory.ToArray());
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotCraft.Unity", "attach");

    internal static string ExtractNative(string root)
    {
        using var stream = typeof(UnityAttachService).Assembly.GetManifestResourceStream("DotCraft.Unity.Native.dll")
            ?? throw new PlatformNotSupportedException("This distribution does not include the Windows x64 Attach bootstrap. Build the native resource before publishing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var directory = Path.Combine(root, "native", Convert.ToHexString(SHA256.HashData(bytes)));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "DotCraft.Unity.Native.dll");
        if (!File.Exists(path))
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporary, bytes);
            try { File.Move(temporary, path, false); }
            catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
        }
        return path;
    }

    internal static string Connection(string root, int pid)
    {
        using var process = Process.GetProcessById(pid);
        return Path.Combine(root, "sessions", $"{pid}-{process.StartTime.ToUniversalTime().Ticks}.json");
    }

    internal static async Task<FileStream> Lock(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, cancellationToken); }
        }
    }

    internal static void Write(string path, JsonObject value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, value.ToJsonString());
        File.Move(temporary, path, true);
    }

    internal static string? Project(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path))?["project"]?.GetValue<string>(); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }
}
