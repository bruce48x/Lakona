using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

internal interface IClientNotificationCommandRouter
{
    ValueTask<ClientNotificationStatus> DispatchGeneratedAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default);
}
