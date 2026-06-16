using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

internal sealed class NoopClientNotificationRemoteDispatcher : IClientNotificationRemoteDispatcher
{
    public ValueTask<ClientNotificationStatus> DispatchAsync<TCallback>(
        RouteLocation target,
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Failed);
    }
}
