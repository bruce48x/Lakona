using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    internal static class ClusterProtocol
    {
        public const string Identifier = "lakona.cluster.v1";

        public const int ServiceId = 0x554C4301;

        public static class Methods
        {
            public const int ActorAsk = 1;
            public const int ActorTell = 2;
            public const int ActorLocationLookup = 3;
            public const int ActorLocationRegister = 4;
            public const int ActorLocationUnregister = 5;
            public const int ActorLocationRegistrySnapshot = 6;
            public const int ActorLifecycleCreate = 7;
            public const int ActorLifecycleDestroy = 8;
            public const int ClientNotificationDispatch = 9;
            public const int ClientNotificationBatchDispatch = 10;
            public const int StartupAffinityLookup = 11;
            public const int StartupAffinityBind = 12;
            public const int StartupAffinityCatalogLookup = 13;
            public const int StartupAffinityRetain = 14;
            public const int StartupAffinityOwnerSnapshot = 15;
            public const int MembershipFrame = 16;
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
        }

        public static class MembershipSnapshots
        {
            public const byte FormatVersion = 2;
        }

        public static readonly RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>
            MembershipFrameMethod = new RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>(
                ServiceId,
                Methods.MembershipFrame);
    }
}
