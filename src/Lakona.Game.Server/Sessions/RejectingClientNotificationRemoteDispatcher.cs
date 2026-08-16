using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

internal sealed class RejectingClientNotificationRemoteDispatcher : IClientNotificationRemoteDispatcher
{
    public ValueTask<ClientNotificationStatus> DispatchAsync(
        RouteLocation target,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Failed);
    }
}
