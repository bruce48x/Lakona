using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State;
using Gateway.Services;

namespace Gateway.Hosting;

internal sealed class MatchmakingHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IMatchmakingStateStore _matchmaking;
    private readonly ILogger<MatchmakingHostedService> _logger;

    public MatchmakingHostedService(IMatchmakingStateStore matchmaking, ILogger<MatchmakingHostedService> logger)
    {
        _matchmaking = matchmaking;
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
                    await _matchmaking
                        .TickAsync(new MatchmakingTickRequest
                        {
                            ObservedAtUtc = DateTime.UtcNow
                        })
                        .ConfigureAwait(false);
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
