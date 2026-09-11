using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace DotCraft.Unity;

/// <summary>Local Mono Editor connection and target-side execution.</summary>
public sealed class UnityAttachService(string cacheRoot, string nativeBootstrapPath) : IAsyncDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<TargetIdentity, byte> owned = new();
    private readonly ConcurrentDictionary<string, TargetIdentity> selected = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ExecutionHandle> executions = new(StringComparer.Ordinal);
    private readonly string clientId = Guid.NewGuid().ToString("N");
    private bool disposed;
    private string? runtimeIdentity;
    private string? extractedNative;
    private string NativeBootstrapPath => string.IsNullOrEmpty(nativeBootstrapPath) ? extractedNative ??= AttachStorage.ExtractNative(cacheRoot) : nativeBootstrapPath;
    private string RuntimeIdentity => runtimeIdentity ??= AttachStorage.RuntimeIdentity(NativeBootstrapPath);

    /// <summary>Creates an Attach client using the shared per-user rendezvous.</summary>
    public static UnityAttachService CreateDefault() => new(AttachStorage.DefaultRoot, "");

    /// <summary>Lists running local Unity Editors without attaching.</summary>
    public object List()
    {
        var targets = new List<object>();
        foreach (var process in Process.GetProcessesByName("Unity"))
        {
            using (process)
            {
                try
                {
                    targets.Add(new { pid = process.Id, startedUtc = process.StartTime.ToUniversalTime(),
                        editor = process.MainModule?.FileName,
                        project = AttachStorage.Project(Connection(process.Id)) ?? EditorDiscovery.Project(process) });
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
            }
        }
        return targets;
    }

    private string Connection(int pid) => AttachStorage.Connection(cacheRoot, pid);

    /// <summary>Attaches or restores the bridge without replaying an execution.</summary>
    public async Task<JsonObject> Connect(string threadId, int? requestedPid, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var pid = requestedPid ?? RequireTarget(threadId).Pid;
            using var target = GetUnityProcess(pid);
            var identity = new TargetIdentity(pid, target.StartTime.ToUniversalTime());
            var path = Connection(pid);
            using var processLock = await AttachStorage.Lock(path, cancellationToken);
            var journal = path + ".bootstrap";
            if (File.Exists(path))
            {
                var recordedIdentity = JsonNode.Parse(File.ReadAllText(path))?["runtimeIdentity"]?.GetValue<string>();
                if (recordedIdentity != RuntimeIdentity) throw new UnityTargetException("UnityRuntimeVersionMismatch", "The selected Editor has a different Attach runtime. Restart the Editor to upgrade; no injection or shutdown was sent.");
                try
                {
                    var current = await BridgeClient.Call(path, "metadata");
                    if (current["state"]?.GetValue<string>() != "completed")
                        throw new IOException("The bridge has not completed its main-thread metadata handshake.");
                    if (current["result"]?["asyncExecution"]?.GetValue<bool>() == true)
                    {
                        ValidateRuntime(current);
                        if (File.Exists(journal)) File.Delete(journal);
                        await RecordSelection(threadId, identity, current);
                        return current;
                    }
                    throw new UnityTargetException("UnityRuntimeVersionMismatch", "The existing Unity bridge does not support this runtime. Restart the Editor to upgrade; no bridge was stopped or injected.");
                }
                catch (Exception e) when (e is not UnityTargetException && (e is IOException or InvalidDataException or System.Net.Sockets.SocketException or InvalidOperationException or ArgumentException)) { }
            }
            if (File.Exists(path + ".lifecycle") && JsonNode.Parse(File.ReadAllText(path + ".lifecycle"))?["stage"]?.GetValue<int>() != 200)
                return await WaitForHandshake(threadId, identity, target, path, journal, cancellationToken);
            if (!File.Exists(NativeBootstrapPath)) throw new FileNotFoundException("Native bootstrap is not configured.", NativeBootstrapPath);
            if (File.Exists(journal))
                throw new InvalidOperationException("Bootstrap outcome is unknown. No new bootstrap was sent; wait for its handshake or restart the isolated test session.");
            var data = Path.Combine(Path.GetDirectoryName(target.MainModule!.FileName)!, "Data");
            var payloadSource = Path.Combine(cacheRoot, "payload", RuntimeIdentity);
            Directory.CreateDirectory(payloadSource);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var owner = typeof(UnityAttachService).Assembly;
            foreach (var name in owner.GetManifestResourceNames().Where(n => n.EndsWith(".cs", StringComparison.Ordinal)))
            {
                using var reader = new StreamReader(owner.GetManifestResourceStream(name)!);
                AttachStorage.WriteSource(Path.Combine(payloadSource, name), reader.ReadToEnd());
            }
            AttachStorage.WriteSource(Path.Combine(payloadSource, "RuntimeIdentity.cs"),
                "namespace DotCraft.Unity { internal static class RuntimeIdentity { public const string Value = \"" + RuntimeIdentity
                + "\"; public const string Version = \"" + AttachStorage.ProductVersion + "\"; } }");
            var references = Directory.GetFiles(Path.Combine(data, "Managed/UnityEngine"), "*.dll")
                .Concat(new[] { "mscorlib.dll", "System.dll", "System.Core.dll" }.Select(n => Path.Combine(data, "MonoBleedingEdge/lib/mono/unityjit-win32", n)))
                .Append(Path.Combine(data, "Managed/Newtonsoft.Json.dll"))
                .Concat(Directory.GetFiles(Path.Combine(data, "MonoBleedingEdge/lib/mono/unityjit-win32/Facades"), "*.dll"));
            var payload = TargetCompiler.Compile(payloadSource, references, Path.Combine(cacheRoot, "cache"));
            cancellationToken.ThrowIfCancellationRequested();
            AttachStorage.Write(journal, new JsonObject { ["state"] = "dispatched", ["operationId"] = Guid.NewGuid().ToString("N"), ["startedUtc"] = DateTime.UtcNow });
            NativeInjector.Inject(pid, NativeBootstrapPath, payload, path);
            owned.TryAdd(identity, 0);
            return await WaitForHandshake(threadId, identity, target, path, journal, cancellationToken);
        }
        finally { gate.Release(); }
    }

    /// <summary>Reads live connection metadata without reconnecting.</summary>
    public Task<JsonObject> Status(string threadId)
    {
        var target = RequireTarget(threadId);
        return BridgeClient.Call(Connection(target.Pid), "metadata");
    }

    /// <summary>Compiles for the target and starts one execution; failures are never replayed.</summary>
    public async Task<JsonObject> Execute(
        string threadId,
        string code,
        JsonObject? args,
        bool runInBackground,
        int yieldTimeMs,
        CancellationToken cancellationToken = default)
    {
        var target = RequireTarget(threadId);
        await Connect(threadId, target.Pid, cancellationToken);
        var connection = Connection(target.Pid);
        var prepared = await BridgeClient.Prepare(connection, code, cacheRoot);
        var handle = new ExecutionHandle(threadId, target, prepared.Generation, connection);
        PruneExecutions();
        if (executions.Count >= 4096) throw new UnityTargetException("UnityExecutionCapacity", "This client has too many unresolved executions.");
        executions[prepared.ExecutionId] = handle;
        try
        {
            var started = await BridgeClient.Start(connection, prepared, args, cancellationToken);
            if (IsTerminal(started)) return RecordResult(prepared.ExecutionId, started);
            if (runInBackground)
            {
                try
                {
                    return RecordResult(prepared.ExecutionId, await BridgeClient.Wait(connection, prepared.ExecutionId, prepared.Generation,
                        NormalizeWait(yieldTimeMs), terminate: false, cancellationToken: CancellationToken.None));
                }
                catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or TimeoutException)
                {
                    return Unknown(prepared.ExecutionId, prepared.Generation);
                }
            }

            while (true)
            {
                var result = await BridgeClient.Wait(connection, prepared.ExecutionId, prepared.Generation,
                    1000, terminate: false, cancellationToken: cancellationToken);
                if (IsTerminal(result)) return RecordResult(prepared.ExecutionId, result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { await BridgeClient.Wait(connection, prepared.ExecutionId, prepared.Generation, 0, terminate: true, cancellationToken: CancellationToken.None); }
            catch { }
            throw;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or TimeoutException)
        {
            return Unknown(prepared.ExecutionId, prepared.Generation);
        }
    }

    /// <summary>Waits for or cooperatively cancels an execution created by this task.</summary>
    public async Task<JsonObject> Wait(
        string threadId,
        string executionId,
        int yieldTimeMs,
        bool terminate,
        CancellationToken cancellationToken = default)
    {
        PruneExecutions();
        if (!executions.TryGetValue(executionId, out var handle)) return Lost(executionId, "");
        if (!string.Equals(handle.ThreadId, threadId, StringComparison.Ordinal))
            throw new UnityTargetException("UnityExecutionUnavailable", "The Unity execution does not belong to this task or is no longer available.");

        try
        {
            using var process = GetUnityProcess(handle.Target.Pid);
            if (process.StartTime.ToUniversalTime() != handle.Target.StartedUtc)
                return RecordResult(executionId, Lost(executionId, handle.Generation));
            if (!File.Exists(handle.ConnectionPath)
                || !string.Equals(BridgeClient.ReadGeneration(handle.ConnectionPath), handle.Generation, StringComparison.Ordinal))
                return RecordResult(executionId, Lost(executionId, handle.Generation));
            return RecordResult(executionId, await BridgeClient.Wait(handle.ConnectionPath, executionId, handle.Generation,
                NormalizeWait(yieldTimeMs), terminate, cancellationToken));
        }
        catch (UnityTargetException) { return RecordResult(executionId, Lost(executionId, handle.Generation)); }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidDataException or TimeoutException)
        {
            return RecordResult(executionId, Lost(executionId, handle.Generation));
        }
    }

    /// <summary>Releases this task selection without stopping other clients.</summary>
    public async Task<JsonObject> Disconnect(string threadId)
    {
        var target = RequireTarget(threadId);
        selected.TryRemove(threadId, out _);
        if (selected.Values.Contains(target))
            return new JsonObject { ["state"] = "completed", ["result"] = "detached" };
        var result = await BridgeClient.Call(Connection(target.Pid), "detach", clientId: clientId);
        if (result["state"]?.GetValue<string>() == "completed") owned.TryRemove(target, out _);
        return result;
    }

    private static Process GetUnityProcess(int pid)
    {
        Process target;
        try { target = Process.GetProcessById(pid); }
        catch (ArgumentException) { throw new UnityTargetException("UnityTargetUnavailable", "The selected Unity Editor is no longer running. Call unity.list, then unity.connect with a PID."); }
        try
        {
            if (target.HasExited || !target.ProcessName.Equals("Unity", StringComparison.OrdinalIgnoreCase))
                throw new UnityTargetException("UnityTargetUnavailable", "The selected process is not a Unity Editor. Call unity.list, then unity.connect with a PID.");
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private TargetIdentity RequireTarget(string threadId)
    {
        if (!selected.TryGetValue(threadId, out var selection))
            throw new UnityTargetException("UnityTargetRequired", "No Unity Editor is connected for this task. Call unity.list, then unity.connect with a PID.");
        try
        {
            using var process = GetUnityProcess(selection.Pid);
            if (process.StartTime.ToUniversalTime() == selection.StartedUtc) return selection;
        }
        catch (UnityTargetException) { }
        RemoveSelections(selection);
        throw new UnityTargetException("UnityTargetUnavailable", "The selected Unity Editor is no longer running. Call unity.list, then unity.connect with a PID.");
    }

    private async Task RecordSelection(string threadId, TargetIdentity identity, JsonObject response)
    {
        if (response["state"]?.GetValue<string>() != "completed") return;
        ValidateRuntime(response);
        await BridgeClient.Call(Connection(identity.Pid), "lease", clientId: clientId);
        selected[threadId] = identity;
        owned.TryAdd(identity, 0);
    }

    private async Task<JsonObject> WaitForHandshake(
        string threadId,
        TargetIdentity identity,
        Process target,
        string path,
        string journal,
        CancellationToken cancellationToken)
    {
        var response = await AttachHandshake.WaitAsync(async token =>
        {
            target.Refresh();
            if (target.HasExited)
                throw new UnityTargetException("UnityTargetUnavailable", "The selected Unity Editor exited before completing its main-thread handshake.");
            if (!File.Exists(path)) throw new IOException("Unity has not published its bridge metadata.");
            var result = await BridgeClient.Call(path, "metadata", cancellationToken: token);
            if (result["state"]?.GetValue<string>() != "completed")
                throw new IOException("Unity has not completed its main-thread handshake.");
            ValidateRuntime(result);
            return result;
        }, HandshakeTimeout, cancellationToken);
        if (File.Exists(journal)) File.Delete(journal);
        await RecordSelection(threadId, identity, response);
        return response;
    }

    private void ValidateRuntime(JsonObject response)
    {
        if (response["result"]?["runtimeIdentity"]?.GetValue<string>() != RuntimeIdentity)
            throw new UnityTargetException("UnityRuntimeVersionMismatch", "The running Unity bridge belongs to a different runtime build. Restart the Editor to upgrade; no injection or shutdown was sent.");
    }

    private void RemoveSelections(TargetIdentity identity)
    {
        foreach (var pair in selected.Where(pair => pair.Value == identity))
            selected.TryRemove(pair);
    }

    /// <summary>Releases this client from its connected bridges without reconnecting during cleanup.</summary>
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            await Task.WhenAll(owned.Keys.Select(async target =>
            {
                try { await BridgeClient.Call(Connection(target.Pid), "detach", clientId: clientId); }
                catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException
                    or ArgumentException or TimeoutException or System.Net.Sockets.SocketException) { }
            }));
            selected.Clear();
            executions.Clear();
        }
        finally { gate.Release(); }
    }

    private readonly record struct TargetIdentity(int Pid, DateTime StartedUtc);
    private sealed record ExecutionHandle(string ThreadId, TargetIdentity Target, string Generation, string ConnectionPath)
    {
        public DateTime? TerminalUtc { get; set; }
    }

    private JsonObject RecordResult(string executionId, JsonObject result)
    {
        if (IsTerminal(result) && executions.TryGetValue(executionId, out var handle)) handle.TerminalUtc ??= DateTime.UtcNow;
        PruneExecutions();
        return result;
    }

    private void PruneExecutions()
    {
        var terminal = executions.Where(p => p.Value.TerminalUtc != null).OrderBy(p => p.Value.TerminalUtc).ToArray();
        foreach (var item in terminal.Take(Math.Max(0, terminal.Length - 1024)).Concat(terminal.Where(p => p.Value.TerminalUtc < DateTime.UtcNow.AddMinutes(-10))))
            executions.TryRemove(item.Key, out _);
    }

    private static int NormalizeWait(int waitTimeMs) => Math.Clamp(waitTimeMs, 0, 30_000);

    private static bool IsTerminal(JsonObject result) => result["state"]?.GetValue<string>() is "completed" or "failed" or "cancelled" or "lost";

    private static JsonObject Lost(string executionId, string generation) => new()
    {
        ["state"] = "lost",
        ["executionId"] = executionId,
        ["generation"] = generation
    };

    private static JsonObject Unknown(string executionId, string generation) => new()
    {
        ["state"] = "unknown",
        ["executionId"] = executionId,
        ["generation"] = generation
    };
}

/// <summary>A stable target or execution selection error.</summary>
public sealed class UnityTargetException(string code, string message) : InvalidOperationException(message)
{
    /// <summary>The stable machine-readable error code.</summary>
    public string Code { get; } = code;
}
