using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationOwnerDispatcher
{
    private readonly IReliablePushRuntime _owner;
    private readonly IRouteDirectory _routes;
    private readonly NodeId _localNode;

    public ClientNotificationOwnerDispatcher(
        IReliablePushRuntime owner,
        IRouteDirectory routes,
        NodeId localNode)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _localNode = localNode;
    }

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.OwnerKey) ||
            string.IsNullOrWhiteSpace(command.SessionId))
        {
            return ClientNotificationStatus.Failed;
        }

        var session = new GameSessionKey(
            command.OwnerKey,
            command.SessionId);
        var route = await _routes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (route is null ||
            route.Node != _localNode)
        {
            return ClientNotificationStatus.RouteNotFound;
        }

        return await _owner.PublishAsync(session, command, cancellationToken).ConfigureAwait(false);
    }
}
