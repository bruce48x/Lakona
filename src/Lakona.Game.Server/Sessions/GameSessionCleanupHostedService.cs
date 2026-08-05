using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionCleanupHostedService : BackgroundService
{
    private readonly IGameSessionRegistry _directory;
    private readonly IGameSessionResumeTicketStore _tickets;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _handlers;
    private readonly ILogger<GameSessionCleanupHostedService> _logger;
    private readonly SessionCleanupOptions _options;
    private readonly TimeProvider _timeProvider;

    public GameSessionCleanupHostedService(
        IGameSessionRegistry directory,
        IGameSessionResumeTicketStore tickets,
        SessionCleanupOptions options,
        IEnumerable<IGameSessionLifecycleHandler> handlers,
        ILogger<GameSessionCleanupHostedService> logger,
        TimeProvider timeProvider)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(GetInterval(), _timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    public async ValueTask CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var expirations = await _directory.ExpireSessionsAsync(
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var expiration in expirations)
        {
            await _tickets.RevokeAsync(expiration.Session, cancellationToken).ConfigureAwait(false);
            if (expiration.Kind == GameSessionExpirationKind.RetainedTermination ||
                expiration.ConnectionId is not { } connectionId)
            {
                continue;
            }

            var context = new GameSessionBindingContext(
                expiration.Session,
                connectionId);
            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.OnSessionExpiredAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Game session-expired lifecycle handler failed for {ConnectionId}.",
                        connectionId);
                }
            }
        }
    }

    private TimeSpan GetInterval()
    {
        return _options.Interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : _options.Interval;
    }

}
