using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    internal static class ClusterProtocol
    {
        public const string Identifier = "lakona.cluster.v3";

        public const int ServiceId = 0x554C4301;

        public static class Methods
        {
            public const int ActorAsk = 1;
            public const int ActorTell = 2;
            public const int ActorDirectoryLookup = 3;
            public const int ActorDirectoryAcquire = 4;
            public const int ActorDirectoryRelease = 5;
            public const int ActorDirectoryActivationSnapshot = 6;
            public const int ActorLifecycleCreate = 7;
            public const int ActorLifecycleDestroy = 8;
            public const int ClientNotificationDispatch = 9;
            public const int ClientNotificationBatchDispatch = 10;
            public const int StartupAffinityLookup = 11;
            public const int StartupAffinityBind = 12;
            public const int StartupAffinityCatalogLookup = 13;
            public const int StartupAffinityRetain = 14;
            public const int StartupAffinityOwnerSnapshot = 15;
            public const int MembershipProbe = 16;
            public const int MembershipGossip = 17;
            public const int ActorDirectorySnapshot = 18;
            public const int ActorDirectorySnapshotAcknowledge = 19;
        }

        public static readonly RpcMethod<MembershipProbeRequest, MembershipProbeReply>
            MembershipProbeMethod = new(
                ServiceId,
                Methods.MembershipProbe);

        public static readonly RpcMethod<MembershipGossipRequest, MembershipGossipReply>
            MembershipGossipMethod = new(ServiceId, Methods.MembershipGossip);
    }
}
