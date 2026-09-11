using System.Diagnostics;
using System.Text.Json;

namespace DotCraft.Unity.Cli;

internal sealed class ProjectStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    public ProjectStateStore(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        DiscoveryPath = Path.Combine(ProjectRoot, GatewayConstants.DiscoveryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        ManifestPath = Path.Combine(ProjectRoot, GatewayConstants.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string ProjectRoot { get; }

    public string DiscoveryPath { get; }

    public string ManifestPath { get; }

    public ToolManifest ReadManifestOrDefault() => ReadManifestOrDefault(out _);

    public ToolManifest ReadManifestOrDefault(out string source)
    {
        var manifest = TryRead<ToolManifest>(ManifestPath);
        if (manifest is not null && IsValid(manifest))
        {
            source = "cache";
            manifest.Tools = manifest.Tools
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
            return manifest;
        }

        source = "default";
        return DefaultManifest.Create();
    }

    public UnityToolGatewayDiscovery? ReadLiveDiscovery() => ReadLiveDiscovery(out _);

    public UnityToolGatewayDiscovery? ReadLiveDiscovery(out string? error)
    {
        var discovery = TryRead<UnityToolGatewayDiscovery>(DiscoveryPath);
        error = null;
        if (discovery is not null && discovery.ProtocolVersion != GatewayConstants.ProtocolVersion)
        {
            error = $"Unity Gateway protocol mismatch: expected {GatewayConstants.ProtocolVersion}, received {discovery.ProtocolVersion}.";
            return null;
        }
        if (!IsValid(discovery))
        {
            error = "Unity discovery is missing or invalid. Open the project and enable Unity Tool Gateway; Unity may also be reloading.";
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(discovery!.ProcessId);
            if (process.HasExited)
            {
                error = "The Unity process recorded in discovery has exited.";
                return null;
            }
        }
        catch
        {
            error = "The Unity process recorded in discovery is unavailable.";
            return null;
        }

        return discovery;
    }

    private static bool IsValid(ToolManifest manifest)
    {
        if (manifest.ProtocolVersion != GatewayConstants.ProtocolVersion
            || string.IsNullOrWhiteSpace(manifest.Revision)
            || manifest.Tools is null)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        return manifest.Tools.All(tool =>
            tool is not null && !string.IsNullOrWhiteSpace(tool.Name)
            && names.Add(tool.Name)
            && tool.InputSchema.ValueKind == JsonValueKind.Object);
    }

    private static bool IsValid(UnityToolGatewayDiscovery? discovery)
    {
        if (discovery is null
            || discovery.ProtocolVersion != GatewayConstants.ProtocolVersion
            || discovery.ProcessId <= 0
            || string.IsNullOrWhiteSpace(discovery.Token)
            || !Uri.TryCreate(discovery.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return endpoint.Scheme == Uri.UriSchemeHttp
               && endpoint.IsLoopback
               && string.IsNullOrEmpty(endpoint.UserInfo)
               && string.IsNullOrEmpty(endpoint.Query)
               && string.IsNullOrEmpty(endpoint.Fragment)
               && string.Equals(endpoint.AbsolutePath.TrimEnd('/'), "/dotcraft-unity", StringComparison.Ordinal);
    }

    private static T? TryRead<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class DefaultManifest
{
    public static ToolManifest Create()
    {
        using var schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "code": {
              "type": "string",
              "description": "C# method body to compile and execute. Use return to provide a result. Provide either code or path."
            },
            "path": {
              "type": "string",
              "description": "Project-relative or absolute path of a saved C# script to execute instead of code."
            },
            "args": {
              "type": "object",
              "description": "Values passed to the script as the Args JObject."
            },
            "mode": {
              "type": "string",
              "enum": ["editor", "playmode"],
              "description": "Execution mode."
            }
          },
          "additionalProperties": false
        }
        """);

        return new ToolManifest
        {
            ProtocolVersion = GatewayConstants.ProtocolVersion,
            Revision = "builtin:unity_execute_csharp:" + GatewayConstants.ProtocolVersion,
            Tools =
            [
                new ToolManifestEntry
                {
                    Name = GatewayConstants.ExecuteCSharpToolName,
                    Description = "Compile and execute C# in the running Unity Editor, from an inline snippet or a saved script file.",
                    InputSchema = schema.RootElement.Clone()
                }
            ]
        };
    }
}
