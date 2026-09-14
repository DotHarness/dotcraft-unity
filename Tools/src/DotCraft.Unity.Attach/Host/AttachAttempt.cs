using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity;

// Write BEFORE thread creation to close the host-crash window; only positive evidence permits retry.
internal sealed class AttachAttempt(int pid, string? journal = null, string? diagnosticsPath = null)
{
    private static readonly object LogGate = new();
    private readonly string operationId = Guid.NewGuid().ToString("N");
    private readonly string? diagnostics = diagnosticsPath ?? Environment.GetEnvironmentVariable("DOTCRAFT_UNITY_ATTACH_DIAGNOSTICS");
    private bool journalOwned;
    internal void SetJournal(string path) => journal = path;
    internal bool RetrySafe { get; private set; } = true;
    internal string Stage { get; private set; } = "prepare";
    internal static string BuildId => typeof(AttachAttempt).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    internal void Step(string stage, object? details = null)
    {
        Stage = stage;
        Log("stage", details);
    }
    internal void Dispatch(string stage)
    {
        Step(stage);
        WriteJournal("dispatching", false);
        RetrySafe = false;
    }
    internal void NoRemoteEffect()
    {
        RetrySafe = true;
        WriteJournal("failed", true);
    }
    internal void Failed(Exception error)
    {
        Log("failure", new { errorType = error.GetType().FullName,
            errorCode = (error as UnityTargetException)?.Code,
            win32Error = (error as System.ComponentModel.Win32Exception)?.NativeErrorCode,
            hresult = error.HResult, stack = error.StackTrace });
        if (journalOwned)
        {
            try { WriteJournal("failed", RetrySafe); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Log("journal-write-failed", new { errorType = e.GetType().Name }); }
        }
    }
    internal UnityTargetException DescribeFailure(Exception error)
    {
        var code = (error as UnityTargetException)?.Code ?? (Stage switch
        {
            "resolve-paths" => "UnityAttachPathResolutionFailed",
            "load-library" or "load-result" => "UnityAttachRemoteLoadFailed",
            "bootstrap-module" or "bootstrap" or "bootstrap-result" => "UnityAttachBootstrapFailed",
            "handshake" or "recovery-handshake" => "UnityAttachHandshakeFailed",
            "connect" => "UnityAttachConnectionFailed",
            _ => "UnityAttachPreparationFailed"
        });
        return new UnityTargetException(code, $"{Stage}: {error.Message}");
    }
    internal void Log(string kind, object? details = null)
    {
        if (string.IsNullOrWhiteSpace(diagnostics)) return;
        try
        {
            var path = Path.GetFullPath(diagnostics);
            var line = JsonSerializer.Serialize(new { utc = DateTime.UtcNow, build = BuildId,
                operationId, pid, stage = Stage, kind, retrySafe = RetrySafe, details });
            lock (LogGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine("Attach diagnostics could not be written: " + e.GetType().Name);
        }
    }
    private void WriteJournal(string state, bool retrySafe)
    {
        if (journal == null) return;
        AttachStorage.Write(journal, new JsonObject { ["schema"] = 2, ["state"] = state,
            ["retrySafe"] = retrySafe, ["stage"] = Stage, ["operationId"] = operationId,
            ["pid"] = pid, ["startedUtc"] = DateTime.UtcNow });
        journalOwned = true;
    }
    internal static bool CanRetry(JsonNode? record) => record?["schema"]?.GetValue<int>() == 2
        && record?["state"]?.GetValue<string>() == "failed"
        && record?["retrySafe"]?.GetValue<bool>() == true;
}
