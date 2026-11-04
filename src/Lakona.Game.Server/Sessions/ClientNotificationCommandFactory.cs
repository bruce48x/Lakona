using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationCommandFactory(
    IGameSessionNotificationProfileLookup profiles,
    LakonaEndpointRuntimeRegistry serializers)
{
    public async ValueTask<ClientNotificationCommand> CreateAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        RouteLocation? remoteGateway,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        var profile = remoteGateway is null
            ? await profiles.GetLocalAsync(session, cancellationToken).ConfigureAwait(false)
            : await profiles.GetRemoteAsync(remoteGateway, session, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(profile))
            throw new InvalidOperationException("The target session has no committed notification serializer profile.");

        using var frame = serializers.GetEndpointSerializer(profile).SerializeFrame(payload);
        var callbackType = typeof(TCallback);
        return new ClientNotificationCommand
        {
            OwnerKey = session.OwnerKey,
            SessionId = session.SessionId,
            CallbackContractType = callbackType.AssemblyQualifiedName ?? callbackType.FullName ?? "",
            MethodName = methodName,
            ServiceId = serviceId,
            MethodId = methodId,
            SerializerProfile = profile,
            Payload = frame.ToArray()
        };
    }
}
