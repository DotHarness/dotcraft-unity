using System.Text.Json;

namespace DotCraft.Unity.Cli;

internal static class CliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Name == "version")
        {
            Write(command.Json, new { version = UnityTools.ProductVersion, rid = UnityTools.RuntimeIdentifier,
                mcpSdkVersion = UnityTools.McpSdkVersion, buildId = DotCraft.Unity.AttachAttempt.BuildId },
                $"dotcraft-unity {UnityTools.ProductVersion} ({UnityTools.RuntimeIdentifier})");
            return 0;
        }
        var projectRoot = command.ResolveProjectRoot();
        if (command.Name.StartsWith("tools ", StringComparison.Ordinal))
        {
            var entries = UnityTools.All.Where(t => command.Name == "tools list" || t.Name == command.ToolName).ToArray();
            if (entries.Length == 0)
                return WriteError(command.Json, "ToolNotFound", $"Tool '{command.ToolName}' does not exist.", 1);
            Write(command.Json, new { success = true, projectRoot, tools = entries },
                string.Join("\n", entries.Select(t => command.Name == "tools list" ? $"{t.Name}: {t.Description}"
                    : $"{t.Name}: {t.Description}\n{t.InputSchema}")));
            return 0;
        }
        await using var client = new AttachToolClient(projectRoot, command.ProcessId);
        if (command.Name == "status")
        {
            var targets = client.ListTargets().Where(t => !command.ProcessId.HasValue || t.Pid == command.ProcessId)
                .Where(t => t.Project != null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(t.Project))
                    .Equals(Path.TrimEndingDirectorySeparator(projectRoot), StringComparison.OrdinalIgnoreCase)).ToArray();
            var described = targets.Select(t => new { t.Pid, t.Project, t.Editor,
                editorState = DescribeState(client.PeekState(t.Pid)) }).ToArray();
            Write(command.Json, new { success = targets.Length == 1, projectRoot, targets = described,
                message = "Discovery only; no injection was performed. editorState is what the bridge last " +
                    "published and is absent until this Editor has been attached; it does not establish readiness on its own." },
                "Attach candidates:\n" + string.Join("\n",
                    described.Select(t => $"{t.Pid}: {t.Project}\n  {StateText(t.editorState)}")));
            return targets.Length == 1 ? 0 : 1;
        }
        var arguments = command.Name switch
        {
            "call" => await ReadObjectAsync(command.Get("--arguments"), command.Get("--arguments-file"), cancellationToken),
            "recompile" => command.Get("--wait-ms") is { } waitMs
                ? new Dictionary<string, JsonElement> { ["yieldTimeMs"] = JsonSerializer.SerializeToElement(int.Parse(waitMs)) }
                : new Dictionary<string, JsonElement>(),
            _ => await BuildScriptArgumentsAsync(command, cancellationToken)
        };
        var name = command.Name switch
        {
            "call" => command.ToolName!,
            "recompile" => UnityTools.Recompile,
            _ => UnityTools.ExecuteCSharp
        };
        var result = await client.CallAsync(name, arguments, cancellationToken);
        Write(command.Json, result, result.Text ?? result.ErrorMessage ?? JsonSerializer.Serialize(result.Result, JsonOptions));
        return result.Success ? 0 : 1;
    }

    private static async Task<Dictionary<string, JsonElement>> BuildScriptArgumentsAsync(CommandLine command, CancellationToken ct)
    {
        var args = await ReadObjectAsync(command.Get("--args"), command.Get("--args-file"), ct);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["args"] = JsonSerializer.SerializeToElement(args),
            ["mode"] = JsonSerializer.SerializeToElement(command.Get("--mode") ?? "editor")
        };
        if (command.Get("--path") is { } path)
            arguments["path"] = JsonSerializer.SerializeToElement(path);
        else
        {
            var code = command.Has("--stdin") ? await Console.In.ReadToEndAsync(ct) : command.Get("--code")!;
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("C# code must not be empty.");
            arguments["code"] = JsonSerializer.SerializeToElement(code);
        }
        return arguments;
    }

    private static async Task<Dictionary<string, JsonElement>> ReadObjectAsync(string? json, string? file, CancellationToken ct)
    {
        if (file != null)
            json = file == "-" ? await Console.In.ReadToEndAsync(ct) : await File.ReadAllTextAsync(file, ct);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json ?? "{}")
            ?? throw new ArgumentException("Tool arguments and script args must be JSON objects.");
    }

    private static object? DescribeState(UnityEditorState? state)
    {
        if (state?.Bridge is not { } bridge) return null;
        return new
        {
            connection = state.Connection.ToString(),
            bridge.PlayMode,
            bridge.Compiling,
            bridge.Reloading,
            bridge.DomainEpoch,
            compile = bridge.Compile is { } compile
                ? new { compile.Sequence, finished = compile.FinishedUtc != null, compile.Succeeded, compile.ErrorCount,
                    compile.WarningCount, compile.FinishedUtc }
                : null
        };
    }

    private static string StateText(object? state) => state == null
        ? "no published Editor state (not attached since the Editor started)"
        : JsonSerializer.Serialize(state, JsonOptions);

    public static int WriteError(bool json, string code, string message, int exitCode)
    {
        if (json)
            Console.Out.WriteLine(JsonSerializer.Serialize(new { success = false, errorCode = code, errorMessage = message }, JsonOptions));
        else
            Console.Error.WriteLine($"{code}: {message}");
        return exitCode;
    }

    private static void Write(bool json, object value, string text) =>
        Console.Out.WriteLine(json ? JsonSerializer.Serialize(value, JsonOptions) : text);
}
