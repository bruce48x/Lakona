using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ClusterProtocol
    {
        public const int ServiceId = 0x554C4301;

        public const int ActorAskMethodId = 2;

        public const int ActorTellMethodId = 3;

        public const int MembershipFrameMethodId = 40;

        public static readonly RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>
            MembershipFrameMethod = new RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>(
                ServiceId,
                MembershipFrameMethodId);
    }
}
