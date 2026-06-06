using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionCleanupHostedService : BackgroundService
{
    private readonly IGameSessionDirectory _directory;
    private readonly SessionCleanupOptions _options;

    public GameSessionCleanupHostedService(
        IGameSessionDirectory directory,
        SessionCleanupOptions options)
    {
        _directory = directory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(GetInterval(), stoppingToken).ConfigureAwait(false);
        }
    }

    public ValueTask CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var disconnectedBefore = DateTimeOffset.UtcNow - GetDisconnectedEndpointRetention();
        return _directory.ExpireDisconnectedEndpointsAsync(disconnectedBefore, cancellationToken);
    }

    private TimeSpan GetInterval()
    {
        return _options.Interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : _options.Interval;
    }

    private TimeSpan GetDisconnectedEndpointRetention()
    {
        return _options.DisconnectedEndpointRetention <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(2)
            : _options.DisconnectedEndpointRetention;
    }
}

