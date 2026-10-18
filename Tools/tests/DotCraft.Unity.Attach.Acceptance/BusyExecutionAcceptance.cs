using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.Unity;

internal static class BusyExecutionAcceptance
{
    private const string ThreadId = "acceptance";
    private const string ProbeName = "DotCraftAttachBusyAcceptance";

    public static async Task Run(UnityAttachService client, int pid, JsonObject evidence)
    {
        var settings = await Execute(client, "return new { enabled = EditorSettings.enterPlayModeOptionsEnabled, options = (int)EditorSettings.enterPlayModeOptions };");
        try
        {
            foreach (var paused in new[] { false, true })
            {
                await Execute(client, "EditorSettings.enterPlayModeOptionsEnabled = true; EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload; EditorApplication.isPlaying = true; return true;");
                await Until(async () => (await client.Status(ThreadId))["result"]!["playing"]!.GetValue<bool>());
                await Execute(client, "new GameObject(Args[\"name\"].ToString()); EditorApplication.isPaused = (bool)Args[\"paused\"]; UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return true;",
                    new JsonObject { ["name"] = ProbeName, ["paused"] = paused });
                await Until(async () => (await client.Status(ThreadId))["result"]!["compiling"]!.GetValue<bool>());
                await InspectAndExit(client, pid, evidence, paused);
            }
        }
        finally
        {
            await Execute(client, "EditorSettings.enterPlayModeOptionsEnabled = (bool)Args[\"enabled\"]; EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)(int)Args[\"options\"]; return true;", settings.AsObject());
        }
    }

    private static async Task InspectAndExit(UnityAttachService client, int pid, JsonObject evidence, bool paused)
    {
        var query = await Execute(client, "return new { playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused, compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating, found = GameObject.Find(Args[\"name\"].ToString()) != null };",
            new JsonObject { ["name"] = ProbeName });
        Require(query["playing"]!.GetValue<bool>() && query["compiling"]!.GetValue<bool>(), "The compilation was not pending in Play Mode.");
        Require(query["found"]!.GetValue<bool>(), "A query could not read the runtime object while compilation was pending.");
        Require(query["paused"]!.GetValue<bool>() == paused, "Unexpected Play Mode pause state.");

        var failure = await client.Execute(ThreadId, "throw new InvalidOperationException(\"busy-acceptance-exception\");", null, false, 1000);
        Require(failure["state"]!.GetValue<string>() == "failed" && failure["error"]!.GetValue<string>().Contains("busy-acceptance-exception"), "The snippet's actual exception was not returned.");

        var before = await client.Status(ThreadId);
        var marker = "dotcraft-busy-exit-" + Guid.NewGuid().ToString("N");
        var exit = await client.Execute(ThreadId, "SessionState.SetInt(Args[\"key\"].ToString(), SessionState.GetInt(Args[\"key\"].ToString(), 0) + 1); EditorApplication.isPaused = false; EditorApplication.isPlaying = false; return true;",
            new JsonObject { ["key"] = marker }, false, 1000);
        Require(exit["state"]!.GetValue<string>() is "completed" or "lost" or "unknown", "The exit Play Mode request was rejected: " + exit);

        await Until(async () =>
        {
            try
            {
                var status = await client.Connect(ThreadId, pid);
                var result = status["result"]!;
                return !result["playing"]!.GetValue<bool>() && !result["compiling"]!.GetValue<bool>() && !result["updating"]!.GetValue<bool>();
            }
            catch (IOException) { return false; }
            catch (TimeoutException) { return false; }
            catch (InvalidOperationException error) when (error is not UnityTargetException) { return false; }
        });

        var after = await client.Status(ThreadId);
        var count = await Execute(client, "return SessionState.GetInt(Args[\"key\"].ToString(), 0);", new JsonObject { ["key"] = marker });
        Require(count.GetValue<int>() == 1, "The exit request did not run exactly once.");
        Require(client.GetEditorState(ThreadId)?.Bridge?.Compile is { FinishedUtc: not null, Succeeded: true }, "The pending compilation did not finish successfully.");
        evidence[paused ? "pausedCompilationPending" : "playingCompilationPending"] = new JsonObject
        {
            ["query"] = query.DeepClone(),
            ["exitState"] = exit["state"]!.DeepClone(),
            ["exitExecutionCount"] = count.DeepClone(),
            ["domainReloaded"] = before["generation"]!.GetValue<string>() != after["generation"]!.GetValue<string>()
        };
    }

    private static async Task<JsonNode> Execute(UnityAttachService client, string code, JsonObject? args = null)
    {
        var result = await client.Execute(ThreadId, code, args, false, 1000);
        Require(result["state"]!.GetValue<string>() == "completed", "Snippet failed: " + result);
        return result["result"]!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task Until(Func<Task<bool>> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(90))
        {
            if (await predicate()) return;
            await Task.Delay(250);
        }
        throw new TimeoutException("The isolated Editor did not reach the required state within 90 seconds.");
    }
}
