using System.Text.Json;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

internal static class ClientNotificationCommandFactory
{
    internal static ClientNotificationCommand CreateGenerated<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload)
        where TCallback : class
    {
        var callbackType = typeof(TCallback);
        return new ClientNotificationCommand
        {
            OwnerKey = session.OwnerKey,
            SessionId = session.SessionId,
            CallbackContractType = callbackType.AssemblyQualifiedName ?? callbackType.FullName ?? "",
            MethodName = methodName,
            ServiceId = serviceId,
            MethodId = methodId,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload)
        };
    }
}
