using DotCraft.Unity;
using System.Diagnostics;
using System.Text.Json.Nodes;

internal static class LifecycleBoundaryAcceptance
{
    public static async Task Run(UnityAttachService parent, int pid, string project, string evidencePath, JsonObject evidence)
    {
        using var editor = Process.GetProcessById(pid);
        var sessionName = $"{pid}-{editor.StartTime.ToUniversalTime().Ticks}.json";
        var stableRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotCraft.Unity", "attach");
        var stableSession = Path.Combine(stableRoot, "sessions", sessionName);
        var temporaryRoot = Path.Combine(Path.GetDirectoryName(evidencePath)!, "unknown-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporaryRoot, "sessions"));
        var journal = Path.Combine(temporaryRoot, "sessions", sessionName + ".bootstrap");
        const string unknown = "{\"state\":\"dispatched\",\"operationId\":\"simulated-crashed-host\"}";
        File.WriteAllText(journal, unknown);
        var writtenUtc = File.GetLastWriteTimeUtc(journal);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var fresh = new UnityAttachService(temporaryRoot, typeof(UnityAttachService).Assembly.Location);
                try { await fresh.Connect("unknown-journal", pid); throw new Exception("Unknown bootstrap was accepted."); }
                catch (InvalidOperationException error) when (error.Message.Contains("outcome is unknown", StringComparison.Ordinal)) { }
                Require(File.ReadAllText(journal) == unknown && File.GetLastWriteTimeUtc(journal) == writtenUtc, "Unknown dispatch journal was rewritten.");
                Require(!File.Exists(Path.Combine(temporaryRoot, "sessions", sessionName)) && !Directory.Exists(Path.Combine(temporaryRoot, "payload")), "Unknown bootstrap dispatched new work.");
            }
            evidence["persistedUnknownBootstrapNoReplay"] = true;
        }
        finally { Directory.Delete(temporaryRoot, true); }

        var originalSession = File.ReadAllBytes(stableSession);
        await using (var incompatible = new UnityAttachService(stableRoot, typeof(UnityAttachService).Assembly.Location))
        {
            try { await incompatible.Connect("wrong-runtime", pid); throw new Exception("Incompatible runtime was accepted."); }
            catch (UnityTargetException error) when (error.Code == "UnityRuntimeVersionMismatch") { }
        }
        Require(originalSession.SequenceEqual(File.ReadAllBytes(stableSession)), "Incompatible runtime rewrote the active rendezvous.");
        evidence["incompatibleRuntimeRejected"] = true;

        using (var crashed = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { typeof(LifecycleBoundaryAcceptance).Assembly.Location, pid.ToString(), project, evidencePath, "peer" },
            RedirectStandardOutput = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true
        })!)
        {
            evidence["crashedPeerLeasePruned"] = false;
            Require(await crashed.StandardOutput.ReadLineAsync() == "ready", "Crash-test peer did not connect.");
            var peerPid = crashed.Id;
            var leases = stableSession + ".leases";
            Require(HasLease(leases, peerPid), "Peer connection did not persist a lease.");
            crashed.Kill();
            await crashed.WaitForExitAsync();
            await Until(async () =>
            {
                await parent.Status("acceptance");
                return !HasLease(leases, peerPid);
            }, TimeSpan.FromSeconds(20));
            var result = await parent.Execute("acceptance", "return 9;", null, false, 1000);
            Require(result["state"]?.GetValue<string>() == "completed" && result["result"]?.GetValue<int>() == 9, "The parent stopped functioning after peer termination.");
            evidence["crashedPeerLeasePruned"] = true;
        }

        var marker = Path.Combine(Path.GetDirectoryName(evidencePath)!, "busy-" + Guid.NewGuid().ToString("N"));
        var counter = "attach-busy-" + Guid.NewGuid().ToString("N");
        var originalGeneration = (await parent.Status("acceptance"))["generation"]!.GetValue<string>();
        try
        {
            evidence["busyMainThreadReacquiredWithoutReplay"] = false;
            var busy = await parent.Execute("acceptance", "SessionState.SetInt(Args[\"counter\"].ToString(), SessionState.GetInt(Args[\"counter\"].ToString(), 0) + 1); System.IO.File.WriteAllText(Args[\"marker\"].ToString(), \"started\"); System.Threading.Thread.Sleep(12000); return SessionState.GetInt(Args[\"counter\"].ToString(), 0);", new JsonObject { ["marker"] = marker, ["counter"] = counter }, true, 0);
            await Until(() => Task.FromResult(File.Exists(marker)), TimeSpan.FromSeconds(10));
            await using var observer = UnityAttachService.CreateDefault();
            var elapsed = Stopwatch.StartNew();
            var recovered = await observer.Connect("busy-observer", pid);
            Require(recovered["state"]?.GetValue<string>() == "completed" && recovered["generation"]?.GetValue<string>() == originalGeneration, "Busy main thread changed or lost the selected bridge.");
            Require(elapsed.Elapsed >= TimeSpan.FromSeconds(10), "The metadata probe did not span the deliberate main-thread blockage.");
            var completion = await parent.Wait("acceptance", busy["executionId"]!.GetValue<string>(), 1000, false);
            Require(completion["state"]?.GetValue<string>() == "completed" && completion["result"]?.GetValue<int>() == 1, "Busy user execution did not complete exactly once.");
            evidence["busyMainThreadReacquiredWithoutReplay"] = true;
            evidence["busyMetadataWaitMs"] = elapsed.ElapsedMilliseconds;
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
    }

    private static bool HasLease(string path, int pid)
    {
        try { return JsonNode.Parse(File.ReadAllText(path))!.AsObject().Any(pair => pair.Value?["Pid"]?.GetValue<int>() == pid); }
        catch (System.Text.Json.JsonException) { return true; }
        catch (IOException) { return true; }
    }

    private static async Task Until(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Lifecycle boundary evidence was not observed within its deadline.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
