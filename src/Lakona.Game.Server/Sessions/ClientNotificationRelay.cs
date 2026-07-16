using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationRelay : IClientNotificationRelay
{
    private readonly IGameSessionRegistry _sessions;
    private readonly GameSessionCallbackResolver? _callbacks;
    private readonly IRouteDirectory? _routes;
    private readonly IClientNotificationRemoteDispatcher? _remoteDispatcher;
    private readonly NodeId? _localNode;

    public ClientNotificationRelay(IGameSessionRegistry sessions)
        : this(sessions, null, null, null, null)
    {
    }

    public ClientNotificationRelay(
        IGameSessionRegistry sessions,
        IRouteDirectory? routes,
        IClientNotificationRemoteDispatcher? remoteDispatcher,
        NodeId? localNode)
        : this(sessions, null, routes, remoteDispatcher, localNode)
    {
    }

    public ClientNotificationRelay(
        IGameSessionRegistry sessions,
        GameSessionCallbackResolver? callbacks,
        IRouteDirectory? routes,
        IClientNotificationRemoteDispatcher? remoteDispatcher,
        NodeId? localNode)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _callbacks = callbacks;
        _routes = routes;
        _remoteDispatcher = remoteDispatcher;
        _localNode = localNode;
    }

    public async ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(notify);
        cancellationToken.ThrowIfCancellationRequested();

        var callback = _callbacks is null
            ? await _sessions.GetCallbackAsync<TCallback>(session, cancellationToken).ConfigureAwait(false)
            : await _callbacks.ResolveAsync<TCallback>(session, cancellationToken).ConfigureAwait(false);
        if (callback is null)
        {
            return await TryNotifyRemoteAsync(session, notify, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await notify(callback).ConfigureAwait(false);
            return ClientNotificationStatus.Accepted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }

    public ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(notify);
        return NotifyAsync<TCallback>(
            session,
            callback =>
            {
                notify(callback);
                return default;
            },
            cancellationToken);
    }

    private async ValueTask<ClientNotificationStatus> TryNotifyRemoteAsync<TCallback>(
        GameSessionKey session,
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        if (_routes is null || _remoteDispatcher is null || _localNode is null)
        {
            return ClientNotificationStatus.RouteNotFound;
        }

        var route = await _routes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (route is null || route.Generation != session.Generation)
        {
            return ClientNotificationStatus.RouteNotFound;
        }

        if (route.Node == _localNode.Value)
        {
            return ClientNotificationStatus.CallbackUnavailable;
        }

        try
        {
            var command = ClientNotificationCommandFactory.Create(session, notify);
            if (command is null)
            {
                return ClientNotificationStatus.Failed;
            }

            return await _remoteDispatcher
                .DispatchAsync(route, command, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }
}
