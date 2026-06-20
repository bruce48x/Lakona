using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.App.Hotfix;
using Server.App.Services;

namespace Server.App.Hosting;

internal sealed class DisconnectedSessionCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectGracePeriod = TimeSpan.FromSeconds(60);

    private readonly SessionDirectory _sessionDirectory;
    private readonly AgarHotfixRuntimeEvents _hotfixEvents;
    private readonly ILogger<DisconnectedSessionCleanupHostedService> _logger;

    public DisconnectedSessionCleanupHostedService(
        SessionDirectory sessionDirectory,
        AgarHotfixRuntimeEvents hotfixEvents,
        ILogger<DisconnectedSessionCleanupHostedService> logger)
    {
        _sessionDirectory = sessionDirectory;
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
                await CleanupExpiredSessionsAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Disconnected session cleanup hosted service stopped.");
        }
    }

    private async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var expired = _sessionDirectory.GetExpiredControlDisconnects(DateTime.UtcNow, ReconnectGracePeriod);
        foreach (var registration in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _hotfixEvents
                    .CleanupExpiredSessionAsync(
                        registration.PlayerId,
                        registration.ConnectionId,
                        DateTime.UtcNow,
                        "Reconnect grace period expired",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired disconnected session for player {PlayerId}.", registration.PlayerId);
                continue;
            }
        }
    }
}
