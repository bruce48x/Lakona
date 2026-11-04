using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

internal static class SessionNotificationProfileProtocol
{
    public static readonly RpcMethod<GameSessionKey, SessionNotificationProfileReply> Lookup =
        new(ClusterProtocol.ServiceId, ClusterProtocol.Methods.SessionNotificationProfileLookup);

    public static void Bind(RpcServiceRegistry registry, IGameSessionNotificationProfileLookup profiles,
        IClusterMembership membership, NodeId localNode)
    {
        var service = registry.RegisterSingleton(ClusterProtocol.ServiceId, profiles,
            serviceName: nameof(SessionNotificationProfileProtocol));
        service.Register<GameSessionKey, SessionNotificationProfileReply>(
            ClusterProtocol.Methods.SessionNotificationProfileLookup,
            async (lookup, session, cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(session.OwnerKey) ||
                    !MembershipSessionLocator.TryResolve(session, membership, out var route) ||
                    route!.Node != localNode)
                    return new SessionNotificationProfileReply();

                return new SessionNotificationProfileReply
                {
                    Profile = await lookup.GetLocalAsync(session, cancellationToken).ConfigureAwait(false)
                };
            }, methodName: nameof(Lookup));
    }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class SessionNotificationProfileReply
{
    [MemoryPackOrder(0)]
    public string? Profile { get; set; }
}
