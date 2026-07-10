using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationCommandRouter : IClientNotificationCommandRouter
{
    private readonly IReliablePushRuntime _localOwner;
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

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var session = ToSessionKey(command);

        if (_routes is null || _remoteDispatcher is null || _localNode is null)
        {
            return await _localOwner.PublishAsync(session, command, cancellationToken).ConfigureAwait(false);
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
            return await _localOwner.PublishAsync(session, command, cancellationToken).ConfigureAwait(false);
        }

        command.Metadata = null;
        return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId, command.Generation);
}
