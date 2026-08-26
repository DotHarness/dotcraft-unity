using System.Diagnostics;
using System.Text.Json;

namespace DotCraft.Unity.McpGateway;

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

    public ToolManifest ReadManifestOrDefault()
    {
        var manifest = TryRead<ToolManifest>(ManifestPath);
        if (manifest is not null && IsValid(manifest))
        {
            manifest.Tools = manifest.Tools
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
            return manifest;
        }

        return DefaultManifest.Create();
    }

    public UnityToolGatewayDiscovery? ReadLiveDiscovery()
    {
        var discovery = TryRead<UnityToolGatewayDiscovery>(DiscoveryPath);
        if (!IsValid(discovery))
            return null;

        try
        {
            using var process = Process.GetProcessById(discovery!.ProcessId);
            if (process.HasExited)
                return null;
        }
        catch
        {
            return null;
        }

        return discovery;
    }

    private static bool IsValid(ToolManifest manifest)
    {
        if (manifest.SchemaVersion != GatewayConstants.SchemaVersion
            || !string.Equals(manifest.PackageVersion, GatewayConstants.PackageVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.Revision)
            || manifest.Tools is null)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        return manifest.Tools.All(tool =>
            !string.IsNullOrWhiteSpace(tool.Name)
            && names.Add(tool.Name)
            && tool.InputSchema.ValueKind == JsonValueKind.Object);
    }

    private static bool IsValid(UnityToolGatewayDiscovery? discovery)
    {
        if (discovery is null
            || discovery.SchemaVersion != GatewayConstants.SchemaVersion
            || !string.Equals(discovery.PackageVersion, GatewayConstants.PackageVersion, StringComparison.Ordinal)
            || discovery.ProcessId <= 0
            || string.IsNullOrWhiteSpace(discovery.Token)
            || !Uri.TryCreate(discovery.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return endpoint.Scheme == Uri.UriSchemeHttp
               && endpoint.IsLoopback
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
              "description": "Project-relative path of a saved C# script to execute instead of code, for example .craft/scripts/console-read.cs."
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
            SchemaVersion = GatewayConstants.SchemaVersion,
            PackageVersion = GatewayConstants.PackageVersion,
            Revision = "builtin:unity_execute_csharp:" + GatewayConstants.PackageVersion,
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
