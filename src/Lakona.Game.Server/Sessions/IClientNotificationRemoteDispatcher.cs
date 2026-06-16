using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public interface IClientNotificationRemoteDispatcher
{
    ValueTask<ClientNotificationStatus> DispatchAsync<TCallback>(
        RouteLocation target,
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}
