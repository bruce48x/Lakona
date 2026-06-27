using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationCommandRouter : IClientNotificationCommandRouter
{
    private readonly LocalClientNotificationCommandDispatcher _localDispatcher;
    private readonly IRouteDirectory? _routes;
    private readonly IClientNotificationRemoteDispatcher? _remoteDispatcher;
    private readonly NodeId? _localNode;

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
        var local = await _localDispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
        if (local == ClientNotificationStatus.Delivered ||
            local == ClientNotificationStatus.Failed)
        {
            return local;
        }

        if (_routes is null || _remoteDispatcher is null || _localNode is null)
        {
            return ClientNotificationStatus.RouteNotFound;
        }

        var session = ToSessionKey(command);
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

        return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId, command.Generation);
}
