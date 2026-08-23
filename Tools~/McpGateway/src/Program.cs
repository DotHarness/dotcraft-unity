using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.McpGateway;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase))
            return PrintVersion(args);

        if (!TryParseProjectRoot(args, out var projectRoot, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var stateStore = new ProjectStateStore(projectRoot!);
        var toolGatewayClient = new UnityToolGatewayClient(stateStore);
        var tools = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Array.Empty<string>(),
            ApplicationName = "dotcraft-unity-mcp"
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(stateStore);
        builder.Services.AddSingleton(toolGatewayClient);
        builder.Services.AddSingleton(tools);
        builder.Services.AddSingleton<ToolManifestMonitor>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ToolManifestMonitor>());

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "dotcraft-unity",
                    Title = "DotCraft Unity",
                    Version = GatewayConstants.PackageVersion
                };
                options.ServerInstructions =
                    "Use the available tools to inspect or modify the Unity project. Unity may be temporarily unavailable while the Editor restarts or reloads.";
                options.ToolCollection = tools;
            })
            .WithStdioServerTransport();

        using var host = builder.Build();
        host.Services.GetRequiredService<ToolManifestMonitor>().LoadInitialManifest();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static int PrintVersion(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[1], "--json", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: dotcraft-unity-mcp.exe version --json");
            return 2;
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            version = GatewayConstants.PackageVersion,
            rid = GatewayConstants.RuntimeIdentifier,
            mcpSdkVersion = GatewayConstants.McpSdkVersion
        }));
        return 0;
    }

    private static bool TryParseProjectRoot(string[] args, out string? projectRoot, out string? error)
    {
        projectRoot = null;
        error = null;
        if (args.Length != 2 || !string.Equals(args[0], "--project-root", StringComparison.OrdinalIgnoreCase))
        {
            error = "Usage: dotcraft-unity-mcp.exe --project-root <path>";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[1]))
        {
            error = "Project root is required.";
            return false;
        }

        projectRoot = Path.GetFullPath(args[1]);
        if (!Directory.Exists(projectRoot))
        {
            error = $"Project root does not exist: {projectRoot}";
            return false;
        }

        return true;
    }
}
