using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCraft.Unity;

public enum UnityConnectionState { Ready, Reloading, Recovering, RecoveryFailed, Exited }

public sealed record UnityBridgeRecord(
    string? Generation,
    ulong DomainEpoch,
    string? PlayMode,
    bool Compiling,
    bool Reloading,
    UnityCompileRecord? Compile);

public sealed record UnityCompileRecord(
    int Sequence,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    bool? Succeeded,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<UnityCompilerError>? Errors);

public sealed record UnityCompilerError(string? File, int Line, int Column, string? Message);

public sealed record UnityLifecycle(ulong Epoch, int Stage)
{
    internal const int Dispatching = 100;

    internal bool Settled => Stage is 0 or 200;
}

public sealed record UnityEditorState(
    int Pid,
    DateTime StartedUtc,
    string? Project,
    string? Version,
    string? ConnectionGeneration,
    bool Running,
    UnityBridgeRecord? Bridge,
    UnityLifecycle? Lifecycle)
{
    public UnityConnectionState Connection
    {
        get
        {
            if (!Running) return UnityConnectionState.Exited;
            if (Lifecycle is { Stage: UnityLifecycle.Dispatching } || Bridge is null) return UnityConnectionState.Recovering;
            if (Bridge.Reloading)
            {
                if (Lifecycle is not { } lifecycle || lifecycle.Epoch <= Bridge.DomainEpoch) return UnityConnectionState.Reloading;
                return lifecycle.Settled ? UnityConnectionState.Recovering : UnityConnectionState.RecoveryFailed;
            }
            return string.Equals(Bridge.Generation, ConnectionGeneration, StringComparison.Ordinal)
                ? UnityConnectionState.Ready
                : UnityConnectionState.Recovering;
        }
    }
}

internal sealed class EditorStateFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);

    public UnityEditorState Read(string connectionPath, int pid, DateTime startedUtc, bool running)
    {
        var connection = Load<ConnectionRecord>(connectionPath);
        return new UnityEditorState(pid, startedUtc, connection?.Project, connection?.Version, connection?.Generation, running,
            Load<UnityBridgeRecord>(connectionPath + ".state"), Load<UnityLifecycle>(connectionPath + ".lifecycle"));
    }

    private T? Load<T>(string path) where T : class
    {
        var cached = cache.GetValueOrDefault(path);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return cached?.Value as T;
            if (cached is not null && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
                return cached.Value as T;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var value = JsonSerializer.Deserialize<T>(stream, Json);
            cache[path] = new Entry(file.LastWriteTimeUtc, file.Length, value);
            return value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return cached?.Value as T;
        }
    }

    private sealed record Entry(DateTime LastWriteUtc, long Length, object? Value);

    private sealed record ConnectionRecord(string? Project, string? Version, string? Generation);
}
