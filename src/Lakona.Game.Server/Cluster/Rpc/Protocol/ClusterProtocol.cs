using System.Collections.Generic;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    internal readonly record struct ClusterProtocolMethod(string Name, int Id);
    internal readonly record struct ClusterProtocolFrameKind(string Name, byte Id);

    internal static class ClusterProtocol
    {
        public const string Identifier = "lakona.cluster.memorypack.v3";

        public const int ServiceId = 0x554C4301;

        public static class Methods
        {
            public static readonly ClusterProtocolMethod ActorAsk = new("actor.ask", 2);
            public static readonly ClusterProtocolMethod ActorTell = new("actor.tell", 3);
            public static readonly ClusterProtocolMethod ActorLocationLookup = new("actor-location.lookup", 20);
            public static readonly ClusterProtocolMethod ActorLocationRegister = new("actor-location.register", 21);
            public static readonly ClusterProtocolMethod ActorLocationUnregister = new("actor-location.unregister", 22);
            public static readonly ClusterProtocolMethod ActorLocationRegistrySnapshot = new("actor-location.registry-snapshot", 23);
            public static readonly ClusterProtocolMethod ActorLifecycleCreate = new("actor-lifecycle.create", 25);
            public static readonly ClusterProtocolMethod ActorLifecycleDestroy = new("actor-lifecycle.destroy", 26);
            public static readonly ClusterProtocolMethod ClientNotificationDispatch = new("client-notification.dispatch", 30);
            public static readonly ClusterProtocolMethod ClientNotificationBatchDispatch = new("client-notification.batch-dispatch", 31);
            public static readonly ClusterProtocolMethod StartupAffinityLookup = new("startup-affinity.lookup", 32);
            public static readonly ClusterProtocolMethod StartupAffinityBind = new("startup-affinity.bind", 33);
            public static readonly ClusterProtocolMethod StartupAffinityCatalogLookup = new("startup-affinity.catalog-lookup", 35);
            public static readonly ClusterProtocolMethod StartupAffinityRetain = new("startup-affinity.retain", 36);
            public static readonly ClusterProtocolMethod StartupAffinityOwnerSnapshot = new("startup-affinity.owner-snapshot", 37);
            public static readonly ClusterProtocolMethod MembershipFrame = new("membership.frame", 40);

            public static IReadOnlyList<ClusterProtocolMethod> Active { get; } =
            [
                ActorAsk,
                ActorTell,
                ActorLocationLookup,
                ActorLocationRegister,
                ActorLocationUnregister,
                ActorLocationRegistrySnapshot,
                ActorLifecycleCreate,
                ActorLifecycleDestroy,
                ClientNotificationDispatch,
                ClientNotificationBatchDispatch,
                StartupAffinityLookup,
                StartupAffinityBind,
                StartupAffinityCatalogLookup,
                StartupAffinityRetain,
                StartupAffinityOwnerSnapshot,
                MembershipFrame
            ];

            public static IReadOnlyList<ClusterProtocolMethod> Reserved { get; } =
            [
                new("cluster.send", 1),
                new("route.register", 10),
                new("route.resolve", 11),
                new("route.refresh-lease", 12),
                new("route.expire", 13),
                new("route.clear-by-node", 14),
                new("route.clear-by-node-epoch", 15),
                new("route.unregister", 16),
                new("actor-location.shard-snapshot", 24)
            ];
        }

        public static class MembershipFrames
        {
            public const byte Version = 1;
            public const byte JoinRequest = 1;
            public const byte JoinResponse = 2;
            public const byte AppendRequest = 3;
            public const byte AppendResponse = 4;
            public const byte VoteRequest = 5;
            public const byte VoteResponse = 6;
            public const byte Proof = 7;
            public const byte ProofResponse = 8;
            public const byte PromoteRequest = 9;
            public const byte PromoteResponse = 10;
            public const byte ReadyRequest = 11;
            public const byte ReadyResponse = 12;
            public const byte FormationProbeRequest = 13;
            public const byte FormationProbeResponse = 14;
            public const byte FormationAgreementRequest = 15;
            public const byte FormationAgreementResponse = 16;
            public const byte SnapshotInstallRequest = 17;
            public const byte SnapshotInstallResponse = 18;
            public const byte NotLeaderResponse = 19;
            public const byte MembershipUnavailableResponse = 20;

            public static IReadOnlyList<ClusterProtocolFrameKind> Active { get; } =
            [
                new("join.request", JoinRequest),
                new("join.response", JoinResponse),
                new("append.request", AppendRequest),
                new("append.response", AppendResponse),
                new("vote.request", VoteRequest),
                new("vote.response", VoteResponse),
                new("proof", Proof),
                new("proof.response", ProofResponse),
                new("promote.request", PromoteRequest),
                new("promote.response", PromoteResponse),
                new("ready.request", ReadyRequest),
                new("ready.response", ReadyResponse),
                new("formation-probe.request", FormationProbeRequest),
                new("formation-probe.response", FormationProbeResponse),
                new("formation-agreement.request", FormationAgreementRequest),
                new("formation-agreement.response", FormationAgreementResponse),
                new("snapshot-install.request", SnapshotInstallRequest),
                new("snapshot-install.response", SnapshotInstallResponse),
                new("not-leader.response", NotLeaderResponse),
                new("membership-unavailable.response", MembershipUnavailableResponse)
            ];
        }

        public static class MembershipSnapshots
        {
            public const byte FormatVersion = 2;
        }

        public static readonly RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>
            MembershipFrameMethod = new RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>(
                ServiceId,
                Methods.MembershipFrame.Id);
    }
}
