using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

internal interface IClientNotificationCommandRouter
{
    ClientNotificationStatus EnqueueGenerated<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload)
        where TCallback : class;

    ClientNotificationStatus Enqueue(ClientNotificationCommand command);
}
