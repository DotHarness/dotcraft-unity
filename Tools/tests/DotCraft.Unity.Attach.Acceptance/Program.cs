using DotCraft.Unity;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

var pid = int.Parse(args[0]);
var project = Path.GetFullPath(args[1]);
await using var client = UnityAttachService.CreateDefault();
var connected = await client.Connect("acceptance", pid);
if (!string.Equals(Path.GetFullPath(connected["result"]!["project"]!.GetValue<string>()), project, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The selected Editor does not own the explicitly supplied isolated project.");
if (args.Length > 3 && args[3] == "peer")
{
    Console.WriteLine("ready");
    Console.ReadLine();
    var result = await client.Execute("acceptance", "return 7;", null, false, 1000);
    Console.WriteLine(result.ToJsonString());
    return;
}

var evidence = new JsonObject { ["pid"] = pid, ["project"] = project, ["unityVersion"] = connected["result"]!["version"]!.DeepClone(), ["startedUtc"] = DateTime.UtcNow };
evidence["attachAssemblySha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(UnityAttachService).Assembly.Location)));
evidence["runtimeIdentity"] = connected["result"]!["runtimeIdentity"]!.DeepClone();
var evidencePath = Path.GetFullPath(args[2]);
Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
var suffix = Guid.NewGuid().ToString("N");
var type = "DotCraftAttachAcceptance" + suffix;
var script = Path.Combine(project, "Assets", "Editor", type + ".cs");
Directory.CreateDirectory(Path.GetDirectoryName(script)!);
string? oldGeneration = null;
try
{
    await LifecycleBoundaryAcceptance.Run(client, pid, project, evidencePath, evidence);
    using var peer = Process.Start(new ProcessStartInfo("dotnet")
    {
        ArgumentList = { typeof(Program).Assembly.Location, pid.ToString(), project, evidencePath, "peer" },
        RedirectStandardOutput = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true
    })!;
    Require(await peer.StandardOutput.ReadLineAsync() == "ready", "Peer did not connect.");
    await client.Disconnect("acceptance");
    await peer.StandardInput.WriteLineAsync("continue");
    await peer.StandardInput.FlushAsync();
    var peerResult = JsonNode.Parse(await peer.StandardOutput.ReadLineAsync() ?? throw new IOException("Peer returned no result."))!;
    await peer.WaitForExitAsync();
    Require(peerResult["state"]!.GetValue<string>() == "completed" && peerResult["result"]!.GetValue<int>() == 7, "Detaching one client affected its peer.");
    evidence["multipleClients"] = true;
    connected = await client.Connect("acceptance", pid);
    oldGeneration = connected["generation"]!.GetValue<string>();
    evidence["oldGeneration"] = oldGeneration;
    var before = await client.Execute("acceptance", "return System.Reflection.Assembly.GetExecutingAssembly().Location;", null, false, 1000);

    var execution = await client.Execute("acceptance", "await ctx.WaitFrames(100000); return 1;", null, true, 0);
    var executionId = execution["executionId"]!.GetValue<string>();
    var cancelled = await client.Wait("acceptance", executionId, 1000, true);
    Require(cancelled["state"]!.GetValue<string>() == "cancelled", "Cooperative cancellation did not complete.");
    evidence["cancellation"] = true;

    File.WriteAllText(script, "public static class " + type + " { this is a deliberate compiler error; }");
    var key = "dotcraft-attach-acceptance-" + suffix;
    await client.Execute("acceptance", "SessionState.SetBool(Args[\"key\"].ToString(), false); UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += (path, messages) => { if (messages.Any(m => m.type == UnityEditor.Compilation.CompilerMessageType.Error)) SessionState.SetBool(" + JsonSerializer.Serialize(key) + ", true); }; AssetDatabase.Refresh(); return true;", new JsonObject { ["key"] = key }, false, 1000);
    await Until(async () =>
    {
        var status = await client.Status("acceptance");
        Require(status["generation"]!.GetValue<string>() == oldGeneration, "Compiler failure unexpectedly reloaded the domain.");
        if (status["result"]!["compiling"]!.GetValue<bool>() || status["result"]!["updating"]!.GetValue<bool>()) return false;
        var probe = await client.Execute("acceptance", "return SessionState.GetBool(Args[\"key\"].ToString(), false);", new JsonObject { ["key"] = key }, false, 1000);
        return probe["result"]?.GetValue<bool>() == true;
    });
    evidence["compilationFailureWithoutReload"] = true;

    File.WriteAllText(script, "public static class " + type + " { public static int Value = 45; }");
    await client.Execute("acceptance", "AssetDatabase.Refresh(); return true;", null, false, 1000);
    await Until(async () =>
    {
        try
        {
            var status = await client.Connect("acceptance", pid);
            if (status["generation"]!.GetValue<string>() == oldGeneration) return false;
            if (status["result"]!["compiling"]!.GetValue<bool>() || status["result"]!["updating"]!.GetValue<bool>()) return false;
            evidence["newGeneration"] = status["generation"]!.DeepClone();
            evidence["domainEpoch"] = status["result"]!["domainEpoch"]!.DeepClone();
            return true;
        }
        catch (IOException) { return false; }
        catch (InvalidOperationException error) when (error is not UnityTargetException) { return false; }
    });
    var after = await client.Execute("acceptance", "return System.Reflection.Assembly.GetExecutingAssembly().Location;", null, false, 1000);
    Require(before["result"]!.GetValue<string>() != after["result"]!.GetValue<string>(), "The new domain reused an old compiled snippet identity.");
    evidence["generationCacheInvalidated"] = true;
    Require((await client.Wait("acceptance", executionId, 0, false))["state"]!.GetValue<string>() == "lost", "Old execution handle survived reload.");
    evidence["oldHandleLost"] = true;
    var loaded = await client.Execute("acceptance", "return Dcu.Get(Dcu.Type(Args[\"type\"].ToString()), \"Value\");", new JsonObject { ["type"] = type }, false, 1000);
    Require(loaded["result"]!.GetValue<int>() == 45, "The new domain did not load the changed script.");
    evidence["automaticReloadRecovery"] = true;
    var projection = await client.Execute("acceptance", "var go = new GameObject(\"AttachAcceptance\"); UnityEngine.Object.DestroyImmediate(go); return new { items = Enumerable.Range(0,35).ToArray(), destroyed = go, asset = AssetDatabase.LoadAssetAtPath<MonoScript>(Args[\"asset\"].ToString()) };", new JsonObject { ["asset"] = "Assets/Editor/" + type + ".cs" }, false, 1000);
    Require(projection["result"]!["items"]!.AsArray().Count == 32, "Result collection budget was not applied.");
    Require(projection["result"]!["destroyed"] == null, "Destroyed Unity object did not normalize to null.");
    Require(projection["result"]!["asset"]!["assetPath"]!.GetValue<string>() == "Assets/Editor/" + type + ".cs", "Asset identity did not include its project-relative path.");
    evidence["sharedResultProjection"] = true;
    var firstReloadGeneration = evidence["newGeneration"]!.GetValue<string>();
    await client.Execute("acceptance", "EditorUtility.RequestScriptReload(); return true;", null, false, 1000);
    await Until(async () =>
    {
        try
        {
            var status = await client.Connect("acceptance", pid);
            if (status["generation"]!.GetValue<string>() == firstReloadGeneration) return false;
            if (status["result"]!["compiling"]!.GetValue<bool>() || status["result"]!["updating"]!.GetValue<bool>()) return false;
            evidence["secondReloadGeneration"] = status["generation"]!.DeepClone();
            evidence["secondDomainEpoch"] = status["result"]!["domainEpoch"]!.DeepClone();
            return true;
        }
        catch (IOException) { return false; }
        catch (InvalidOperationException error) when (error is not UnityTargetException) { return false; }
    });
    evidence["twoConsecutiveReloads"] = true;
    var settings = await client.Execute("acceptance", "return new { enabled = EditorSettings.enterPlayModeOptionsEnabled, options = (int)EditorSettings.enterPlayModeOptions };", null, false, 1000);
    try
    {
        await client.Execute("acceptance", "EditorSettings.enterPlayModeOptionsEnabled = true; EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload; EditorApplication.isPlaying = true; return true;", null, false, 1000);
        await Until(async () => (await client.Status("acceptance"))["result"]!["playing"]!.GetValue<bool>());
        await client.Execute("acceptance", "EditorApplication.isPlaying = false; return true;", null, false, 1000);
        await Until(async () => !(await client.Status("acceptance"))["result"]!["playing"]!.GetValue<bool>());
        Require((await client.Status("acceptance"))["generation"]!.GetValue<string>() == evidence["secondReloadGeneration"]!.GetValue<string>(), "Disabled-domain-reload Play Mode changed the bridge generation.");
        evidence["disabledDomainReloadRoundTrip"] = true;
    }
    finally
    {
        await client.Execute("acceptance", "EditorApplication.isPlaying = false; EditorSettings.enterPlayModeOptionsEnabled = (bool)Args[\"enabled\"]; EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)(int)Args[\"options\"]; return true;", settings["result"]!.AsObject(), false, 1000);
    }
    evidence["passed"] = true;
}
catch (Exception error)
{
    evidence["passed"] = false;
    evidence["error"] = error.ToString();
    throw;
}
finally
{
    if (File.Exists(script)) File.Delete(script);
    if (File.Exists(script + ".meta")) File.Delete(script + ".meta");
    try { await client.Execute("acceptance", "AssetDatabase.Refresh(); return true;", null, false, 1000); }
    catch (Exception cleanup) { evidence["cleanupError"] = cleanup.Message; }
    evidence["finishedUtc"] = DateTime.UtcNow;
    File.WriteAllText(evidencePath, evidence.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(evidence.ToJsonString());
}

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task Until(Func<Task<bool>> predicate)
{
    var timeout = Stopwatch.StartNew();
    while (timeout.Elapsed < TimeSpan.FromSeconds(90))
    {
        if (await predicate()) return;
        await Task.Delay(250);
    }
    throw new TimeoutException("The live Editor did not establish the required state within 90 seconds.");
}
