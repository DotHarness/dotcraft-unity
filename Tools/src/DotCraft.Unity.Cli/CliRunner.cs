using System.Net.Sockets;
using System.Text.Json;

namespace DotCraft.Unity.Cli;

internal static class CliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Name == "version")
        {
            Write(command.Json, new { version = GatewayConstants.ProductVersion, rid = GatewayConstants.RuntimeIdentifier,
                protocolVersion = CompilerProtocol.Version, mcpSdkVersion = GatewayConstants.McpSdkVersion,
                buildId = DotCraft.Unity.AttachAttempt.BuildId },
                $"dotcraft-unity {GatewayConstants.ProductVersion} ({GatewayConstants.RuntimeIdentifier})");
            return 0;
        }
        var store = new ProjectStateStore(command.ResolveProjectRoot());
        await using var client = new UnityBackendSession(store, command.Backend, command.ProcessId);
        if (command.Name == "status")
        {
            if (client.Backend == "gateway") return await StatusAsync(command, store, cancellationToken);
            var targets = client.ListAttachTargets().Where(t => !command.ProcessId.HasValue || t.Pid == command.ProcessId)
                .Where(t => t.Project != null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(t.Project))
                    .Equals(Path.TrimEndingDirectorySeparator(store.ProjectRoot), StringComparison.OrdinalIgnoreCase)).ToArray();
            Write(command.Json, new { success = targets.Length == 1, backend = "attach", projectRoot = store.ProjectRoot,
                targets, message = "Discovery only; no injection was performed and Editor readiness is not established." },
                "Attach candidates: " + string.Join(", ", targets.Select(t => $"{t.Pid}: {t.Project}")));
            return targets.Length == 1 ? 0 : 1;
        }
        if (command.Name.StartsWith("tools ", StringComparison.Ordinal))
        {
            var manifest = client.ReadManifest(out var source);
            var entries = command.Name == "tools list" ? manifest.Tools
                : manifest.Tools.Where(t => t.Name == command.ToolName).ToList();
            if (entries.Count == 0 && command.Name == "tools describe")
                return WriteError(command.Json, "ToolNotFound", $"Tool '{command.ToolName}' is absent from the {source} manifest.", 1);
            Write(command.Json, new { success = true, projectRoot = store.ProjectRoot, source,
                protocolVersion = manifest.ProtocolVersion, revision = manifest.Revision, tools = entries },
                $"Tool definitions ({source}; execution availability is decided by Unity):\n" +
                string.Join("\n", entries.Select(t => command.Name == "tools list" ? $"{t.Name}: {t.Description}"
                    : $"{t.Name}: {t.Description}\n{t.InputSchema}")));
            return 0;
        }
        var arguments = command.Name == "call"
            ? await ReadObjectAsync(command.Get("--arguments"), command.Get("--arguments-file"), cancellationToken)
            : await BuildScriptArgumentsAsync(command, cancellationToken);
        var name = command.Name == "call" ? command.ToolName! : GatewayConstants.ExecuteCSharpToolName;
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

    private static async Task<int> StatusAsync(CommandLine command, ProjectStateStore store, CancellationToken ct)
    {
        var discovery = store.ReadLiveDiscovery(out var error);
        var reachable = false;
        if (discovery != null)
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var endpoint = new Uri(discovery.Endpoint);
                await tcp.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
                reachable = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (SocketException) { }
        }
        var targetMatches = !command.ProcessId.HasValue || command.ProcessId == discovery?.ProcessId;
        var success = discovery != null && reachable && targetMatches;
        var message = error ?? (!targetMatches ? "The Gateway belongs to a different Unity process." : reachable ? "TCP endpoint is reachable; this does not establish Editor readiness."
            : "Discovery is valid, but its TCP endpoint is unreachable.");
        Write(command.Json, new { success, backend = "gateway", projectRoot = store.ProjectRoot,
            protocolVersion = GatewayConstants.ProtocolVersion, discoveryValid = discovery != null, processId = discovery?.ProcessId,
            tcpReachable = reachable, message },
            $"Project: {store.ProjectRoot}\nProtocol: {GatewayConstants.ProtocolVersion}\n{message}");
        return success ? 0 : 1;
    }

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
