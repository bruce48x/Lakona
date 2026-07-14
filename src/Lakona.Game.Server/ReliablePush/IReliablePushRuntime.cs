using Lakona.Game.Abstractions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

internal interface IReliablePushRuntime
{
    ValueTask<ClientNotificationStatus> PublishGeneratedAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        return PublishAsync(
            session,
            ClientNotificationCommandFactory.CreateGenerated<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload),
            cancellationToken);
    }

    ValueTask<ClientNotificationStatus> PublishAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default);

    ValueTask ReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<ReliablePushAckOutcome> AckAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default);
}
