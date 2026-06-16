using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotificationRelay : IClientNotificationRelay
{
    private readonly IGameSessionDirectory _sessions;
    private readonly IRouteDirectory? _routes;
    private readonly IClientNotificationRemoteDispatcher? _remoteDispatcher;
    private readonly NodeId? _localNode;

    public ClientNotificationRelay(IGameSessionDirectory sessions)
        : this(sessions, null, null, null)
    {
    }

    public ClientNotificationRelay(
        IGameSessionDirectory sessions,
        IRouteDirectory? routes,
        IClientNotificationRemoteDispatcher? remoteDispatcher,
        NodeId? localNode)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _routes = routes;
        _remoteDispatcher = remoteDispatcher;
        _localNode = localNode;
    }

    public async ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(notify);
        cancellationToken.ThrowIfCancellationRequested();

        var callback = await _sessions.GetCallbackAsync<TCallback>(session, cancellationToken)
            .ConfigureAwait(false);
        if (callback is null)
        {
            return await TryNotifyRemoteAsync(session, notify, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            notify(callback);
            return ClientNotificationStatus.Delivered;
        }
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }

    private async ValueTask<ClientNotificationStatus> TryNotifyRemoteAsync<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify,
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
            return await _remoteDispatcher
                .DispatchAsync(route, session, notify, cancellationToken)
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
