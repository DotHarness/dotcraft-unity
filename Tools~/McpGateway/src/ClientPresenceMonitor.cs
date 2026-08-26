using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace DotCraft.Unity.McpGateway;

/// <summary>
/// Heartbeats the client identity to Unity. Every failure is swallowed so presence can never
/// disturb the tool path.
/// </summary>
internal sealed class ClientPresenceMonitor : BackgroundService
{
    private static readonly TimeSpan IdentityPollInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceProvider _services;
    private readonly ClientPresenceState _presence;
    private readonly UnityToolGatewayClient _toolGatewayClient;

    private TimeSpan _interval = TimeSpan.FromSeconds(GatewayConstants.DefaultHeartbeatSeconds);

    public ClientPresenceMonitor(
        IServiceProvider services,
        ClientPresenceState presence,
        UnityToolGatewayClient toolGatewayClient)
    {
        _services = services;
        _presence = presence;
        _toolGatewayClient = toolGatewayClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = _interval;
            try
            {
                _presence.Observe(_services.GetService<McpServer>());
                delay = await BeatAsync(GatewayConstants.PresenceStateOnline, stoppingToken).ConfigureAwait(false);

                // Poll quickly until the client identifies itself, so Unity shows a real name fast.
                if (!_presence.HasClient)
                    delay = IdentityPollInterval;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // stoppingToken is already cancelled here, so the closing beat needs its own budget. Keep it
        // short: a live Unity answers in milliseconds, and a stale discovery file must not stall exit.
        using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await BeatAsync(GatewayConstants.PresenceStateClosing, closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<TimeSpan> BeatAsync(string state, CancellationToken cancellationToken)
    {
        var ack = await _toolGatewayClient
            .PostPresenceAsync(_presence.BuildRequest(state), cancellationToken)
            .ConfigureAwait(false);

        if (ack is null)
            return TimeSpan.FromSeconds(GatewayConstants.PresenceRetrySeconds);

        _interval = ack.HeartbeatSeconds > 0
            ? TimeSpan.FromSeconds(Math.Clamp(
                ack.HeartbeatSeconds,
                GatewayConstants.MinHeartbeatSeconds,
                GatewayConstants.MaxHeartbeatSeconds))
            : TimeSpan.FromSeconds(GatewayConstants.DefaultHeartbeatSeconds);
        return _interval;
    }
}
