using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity;

internal static class AttachStorage
{
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
