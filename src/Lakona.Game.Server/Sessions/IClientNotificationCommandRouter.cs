using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

internal interface IClientNotificationCommandRouter
{
    ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default);
}
