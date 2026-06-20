using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.App.Hotfix;

namespace Server.App.Hosting;

internal sealed class MatchmakingHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly AgarHotfixRuntimeEvents _hotfixEvents;
    private readonly ILogger<MatchmakingHostedService> _logger;

    public MatchmakingHostedService(AgarHotfixRuntimeEvents hotfixEvents, ILogger<MatchmakingHostedService> logger)
    {
        _hotfixEvents = hotfixEvents;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _hotfixEvents.TickMatchmakingAsync(DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Matchmaking tick failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Matchmaking hosted service stopped.");
        }
    }
}
