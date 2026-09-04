using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.McpGateway;

internal static class McpHost
{
    public static async Task<int> RunAsync(string projectRoot)
    {
        var stateStore = new ProjectStateStore(projectRoot);
        var toolGatewayClient = new UnityToolGatewayClient(stateStore);
        var presence = new ClientPresenceState();
        var tools = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Array.Empty<string>(),
            ApplicationName = "dotcraft-unity"
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(stateStore);
        builder.Services.AddSingleton(toolGatewayClient);
        builder.Services.AddSingleton(presence);
        builder.Services.AddSingleton(tools);
        builder.Services.AddSingleton<ToolManifestMonitor>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ToolManifestMonitor>());
        builder.Services.AddHostedService<ClientPresenceMonitor>();

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
}
