using Microsoft.Extensions.Logging;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionRpcLifecycleObserver : IRpcSessionLifecycleObserver
{
    private readonly IGameSessionRegistry _directory;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _handlers;
    private readonly ILogger<GameSessionRpcLifecycleObserver> _logger;
    private readonly GameConnectionDeliveryPolicyRegistry _deliveryPolicies;
    private readonly GameFrameworkConnectionRegistry? _frameworkConnections;

    public GameSessionRpcLifecycleObserver(
        IGameSessionRegistry directory,
        IEnumerable<IGameSessionLifecycleHandler> handlers,
        ILogger<GameSessionRpcLifecycleObserver> logger)
        : this(directory, handlers, logger, new GameConnectionDeliveryPolicyRegistry())
    {
    }

    public GameSessionRpcLifecycleObserver(
        IGameSessionRegistry directory,
        IEnumerable<IGameSessionLifecycleHandler> handlers,
        ILogger<GameSessionRpcLifecycleObserver> logger,
        GameConnectionDeliveryPolicyRegistry deliveryPolicies)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deliveryPolicies = deliveryPolicies ?? throw new ArgumentNullException(nameof(deliveryPolicies));
        _frameworkConnections = null;
    }

    public GameSessionRpcLifecycleObserver(
        IGameSessionRegistry directory,
        IEnumerable<IGameSessionLifecycleHandler> handlers,
        ILogger<GameSessionRpcLifecycleObserver> logger,
        GameConnectionDeliveryPolicyRegistry deliveryPolicies,
        GameFrameworkConnectionRegistry frameworkConnections)
        : this(directory, handlers, logger, deliveryPolicies)
    {
        _frameworkConnections = frameworkConnections ?? throw new ArgumentNullException(nameof(frameworkConnections));
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
        _deliveryPolicies.Remove(context.ConnectionId);
        _frameworkConnections?.Remove(context.ConnectionId);
        var snapshot = await _directory
            .MarkConnectionDisconnectedAsync(context.ConnectionId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return;
        }

        var sessionContext = new GameSessionBindingContext(
            snapshot.Session,
            snapshot.ConnectionId);
        foreach (var handler in _handlers)
        {
            try
            {
                await handler.OnSessionDisconnectedAsync(sessionContext, cancellationToken)
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
                    "Game session-disconnected lifecycle handler failed for {ConnectionId}.",
                    context.ConnectionId);
            }
        }
    }
}
