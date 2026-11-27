using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.Cli;

internal static class McpHost
{
    public static async Task<int> RunAsync(string projectRoot, int? pid)
    {
        await using var client = new AttachToolClient(projectRoot, pid);
        var tools = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);
        foreach (var definition in UnityTools.All)
            tools.Add(new AttachMcpTool(definition, client));

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Array.Empty<string>(),
            ApplicationName = "dotcraft-unity"
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "dotcraft-unity",
                    Title = "DotCraft Unity",
                    Version = UnityTools.ProductVersion
                };
                options.ServerInstructions =
                    "Use the available tools to inspect or modify the Unity project. Unity may be temporarily unavailable while the Editor restarts or reloads.";
                options.ToolCollection = tools;
            })
            .WithStdioServerTransport();

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
