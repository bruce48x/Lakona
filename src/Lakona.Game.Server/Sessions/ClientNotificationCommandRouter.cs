using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationCommandRouter : IClientNotificationCommandRouter
{
    private readonly IReliablePushRuntime? _localOwner;
    private readonly LocalClientNotificationCommandDispatcher? _localDispatcher;
    private readonly IRouteDirectory? _routes;
    private readonly IClientNotificationRemoteDispatcher? _remoteDispatcher;
    private readonly NodeId? _localNode;

    public ClientNotificationCommandRouter(
        IReliablePushRuntime localOwner,
        IRouteDirectory? routes = null,
        IClientNotificationRemoteDispatcher? remoteDispatcher = null,
        NodeId? localNode = null)
    {
        _localOwner = localOwner ?? throw new ArgumentNullException(nameof(localOwner));
        _routes = routes;
        _remoteDispatcher = remoteDispatcher;
        _localNode = localNode;
    }

    public ClientNotificationCommandRouter(
        LocalClientNotificationCommandDispatcher localDispatcher,
        IRouteDirectory? routes = null,
        IClientNotificationRemoteDispatcher? remoteDispatcher = null,
        NodeId? localNode = null)
    {
        _localDispatcher = localDispatcher ?? throw new ArgumentNullException(nameof(localDispatcher));
        _routes = routes;
        _remoteDispatcher = remoteDispatcher;
        _localNode = localNode;
    }

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var session = ToSessionKey(command);

        if (_routes is null || _remoteDispatcher is null || _localNode is null)
        {
            return await DispatchLocalAsync(session, command, cancellationToken).ConfigureAwait(false);
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
            return await DispatchLocalAsync(session, command, cancellationToken).ConfigureAwait(false);
        }

        command.Metadata = null;
        return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ClientNotificationStatus> DispatchGeneratedAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        if (_routes is null || _remoteDispatcher is null || _localNode is null)
        {
            return await DispatchGeneratedLocalAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken).ConfigureAwait(false);
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
            return await DispatchGeneratedLocalAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        var command = ClientNotificationCommandFactory.CreateGenerated<TCallback, TPayload>(
            session,
            serviceId,
            methodId,
            methodName,
            payload);
        return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<ClientNotificationStatus> DispatchLocalAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken) =>
        _localOwner is not null
            ? _localOwner.PublishAsync(session, command, cancellationToken)
            : _localDispatcher!.DispatchAsync(command, cancellationToken);

    private ValueTask<ClientNotificationStatus> DispatchGeneratedLocalAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken)
        where TCallback : class =>
        _localOwner is not null
            ? _localOwner.PublishGeneratedAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken)
            : _localDispatcher!.DispatchGeneratedAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken);

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId, command.Generation);
}
