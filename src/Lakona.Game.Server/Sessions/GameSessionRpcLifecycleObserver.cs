using Microsoft.Extensions.Logging;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionRpcLifecycleObserver : IRpcSessionLifecycleObserver
{
    private readonly IGameSessionDirectory _directory;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _handlers;
    private readonly ILogger<GameSessionRpcLifecycleObserver> _logger;

    public GameSessionRpcLifecycleObserver(
        IGameSessionDirectory directory,
        IEnumerable<IGameSessionLifecycleHandler> handlers,
        ILogger<GameSessionRpcLifecycleObserver> logger)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask OnSessionStartedAsync(
        RpcSessionLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        var gameContext = new GameConnectionContext(context.ConnectionId, context.DisplayName);
        foreach (var handler in _handlers)
        {
            try
            {
                await handler.OnConnectionOpenedAsync(gameContext, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Game session connection-opened lifecycle handler failed for {ConnectionId}.", context.ConnectionId);
            }
        }
    }

    public async ValueTask OnSessionDisconnectedAsync(
        RpcSessionLifecycleContext context,
        Exception? error,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _directory
            .MarkConnectionDisconnectedAsync(context.ConnectionId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var snapshot in snapshots)
        {
            var endpointContext = new GameEndpointBindingContext(
                snapshot.Endpoint,
                snapshot.ConnectionId,
                snapshot.CallbackContractTypes);
            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.OnEndpointDisconnectedAsync(endpointContext, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Game session endpoint-disconnected lifecycle handler failed for {ConnectionId}.",
                        context.ConnectionId);
                }
            }
        }
    }
}
