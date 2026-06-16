using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public interface IClientNotificationRemoteDispatcher
{
    ValueTask<ClientNotificationStatus> DispatchAsync(
        RouteLocation target,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default);
}
