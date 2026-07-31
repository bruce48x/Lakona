using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ClusterProtocol
    {
        public const int ServiceId = 0x554C4301;

        public const int SendMethodId = 1;

        public const int ActorAskMethodId = 2;

        public const int ActorTellMethodId = 3;

        public const int RegisterRouteMethodId = 10;

        public const int ResolveRouteMethodId = 11;

        public const int RefreshRouteLeaseMethodId = 12;

        public const int ExpireRoutesMethodId = 13;

        public const int ClearRoutesByNodeMethodId = 14;

        public const int ClearRoutesByNodeEpochMethodId = 15;

        public const int UnregisterRouteMethodId = 16;

        public const int MembershipFrameMethodId = 40;

        public static readonly RpcMethod<ClusterSendRequest, ClusterSendReply> SendMethod =
            new RpcMethod<ClusterSendRequest, ClusterSendReply>(ServiceId, SendMethodId);

        public static readonly RpcMethod<RouteRegisterRequest, RouteRegisterReply> RegisterRouteMethod =
            new RpcMethod<RouteRegisterRequest, RouteRegisterReply>(ServiceId, RegisterRouteMethodId);

        public static readonly RpcMethod<RouteResolveRequest, RouteResolveReply> ResolveRouteMethod =
            new RpcMethod<RouteResolveRequest, RouteResolveReply>(ServiceId, ResolveRouteMethodId);

        public static readonly RpcMethod<RouteUnregisterRequest, RouteUnregisterReply> UnregisterRouteMethod =
            new RpcMethod<RouteUnregisterRequest, RouteUnregisterReply>(ServiceId, UnregisterRouteMethodId);

        public static readonly RpcMethod<RouteRefreshLeaseRequest, RouteRefreshLeaseReply> RefreshRouteLeaseMethod =
            new RpcMethod<RouteRefreshLeaseRequest, RouteRefreshLeaseReply>(ServiceId, RefreshRouteLeaseMethodId);

        public static readonly RpcMethod<RouteExpireRequest, RouteExpireReply> ExpireRoutesMethod =
            new RpcMethod<RouteExpireRequest, RouteExpireReply>(ServiceId, ExpireRoutesMethodId);

        public static readonly RpcMethod<RouteClearByNodeRequest, RouteClearReply> ClearRoutesByNodeMethod =
            new RpcMethod<RouteClearByNodeRequest, RouteClearReply>(ServiceId, ClearRoutesByNodeMethodId);

        public static readonly RpcMethod<RouteClearByNodeEpochRequest, RouteClearReply> ClearRoutesByNodeEpochMethod =
            new RpcMethod<RouteClearByNodeEpochRequest, RouteClearReply>(ServiceId, ClearRoutesByNodeEpochMethodId);

        public static readonly RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>
            MembershipFrameMethod = new RpcMethod<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>(
                ServiceId,
                MembershipFrameMethodId);
    }
}
