using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionCleanupHostedService : BackgroundService
{
    private readonly IGameSessionDirectory _directory;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _handlers;
    private readonly ILogger<GameSessionCleanupHostedService> _logger;
    private readonly SessionCleanupOptions _options;

    public GameSessionCleanupHostedService(
        IGameSessionDirectory directory,
        SessionCleanupOptions options)
        : this(directory, options, Array.Empty<IGameSessionLifecycleHandler>(), NullLogger<GameSessionCleanupHostedService>.Instance)
    {
    }

    public GameSessionCleanupHostedService(
        IGameSessionDirectory directory,
        SessionCleanupOptions options,
        IEnumerable<IGameSessionLifecycleHandler> handlers,
        ILogger<GameSessionCleanupHostedService> logger)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(GetInterval(), stoppingToken).ConfigureAwait(false);
        }
    }

    public async ValueTask CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var disconnectedBefore = DateTimeOffset.UtcNow - GetDisconnectedEndpointRetention();
        var snapshots = await _directory.ExpireDisconnectedEndpointsAsync(disconnectedBefore, cancellationToken)
            .ConfigureAwait(false);

        foreach (var snapshot in snapshots)
        {
            var context = new GameEndpointBindingContext(
                snapshot.Endpoint,
                snapshot.ConnectionId,
                snapshot.CallbackContractTypes);
            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.OnEndpointExpiredAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Game session endpoint-expired lifecycle handler failed for {ConnectionId}.",
                        snapshot.ConnectionId);
                }
            }
        }
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
