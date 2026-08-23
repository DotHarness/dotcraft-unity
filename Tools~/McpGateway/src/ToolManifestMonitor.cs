using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.McpGateway;

internal sealed class ToolManifestMonitor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private readonly ProjectStateStore _stateStore;
    private readonly UnityToolGatewayClient _toolGatewayClient;
    private readonly McpServerPrimitiveCollection<McpServerTool> _tools;
    private readonly ILogger<ToolManifestMonitor> _logger;
    private string? _revision;

    public ToolManifestMonitor(
        ProjectStateStore stateStore,
        UnityToolGatewayClient toolGatewayClient,
        McpServerPrimitiveCollection<McpServerTool> tools,
        ILogger<ToolManifestMonitor> logger)
    {
        _stateStore = stateStore;
        _toolGatewayClient = toolGatewayClient;
        _tools = tools;
        _logger = logger;
    }

    public void LoadInitialManifest() => Apply(_stateStore.ReadManifestOrDefault());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Apply(_stateStore.ReadManifestOrDefault());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh the Unity tool manifest.");
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private void Apply(ToolManifest manifest)
    {
        if (string.Equals(_revision, manifest.Revision, StringComparison.Ordinal))
            return;

        using (_tools.DeferChangedEvents())
        {
            _tools.Clear();
            foreach (var entry in manifest.Tools)
                _tools.Add(new UnityProxyTool(entry, _toolGatewayClient));
        }

        _revision = manifest.Revision;
        _logger.LogInformation(
            "Loaded Unity tool manifest {Revision} with {ToolCount} tools.",
            manifest.Revision,
            manifest.Tools.Count);
    }
}
