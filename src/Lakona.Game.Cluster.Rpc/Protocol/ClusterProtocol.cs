using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ClusterProtocol
    {
        public const int ServiceId = 0x554C4301;

        public const int SendMethodId = 1;

        public const int RegisterRouteMethodId = 10;

        public const int ResolveRouteMethodId = 11;

        public const int RefreshRouteLeaseMethodId = 12;

        public const int ExpireRoutesMethodId = 13;

        public const int ClearRoutesByNodeMethodId = 14;

        public const int ClearRoutesByNodeEpochMethodId = 15;

        public const int RegisterNodeMethodId = 20;

        public const int HeartbeatNodeMethodId = 21;

        public const int UpdateNodeStateMethodId = 22;

        public const int ResolveNodeMethodId = 23;

        public const int QueryNodesMethodId = 24;

        public const int ExpireNodesMethodId = 25;

        public static readonly RpcMethod<ClusterSendRequest, ClusterSendReply> SendMethod =
            new RpcMethod<ClusterSendRequest, ClusterSendReply>(ServiceId, SendMethodId);

        public static readonly RpcMethod<RouteRegisterRequest, RouteRegisterReply> RegisterRouteMethod =
            new RpcMethod<RouteRegisterRequest, RouteRegisterReply>(ServiceId, RegisterRouteMethodId);

        public static readonly RpcMethod<RouteResolveRequest, RouteResolveReply> ResolveRouteMethod =
            new RpcMethod<RouteResolveRequest, RouteResolveReply>(ServiceId, ResolveRouteMethodId);

        public static readonly RpcMethod<RouteRefreshLeaseRequest, RouteRefreshLeaseReply> RefreshRouteLeaseMethod =
            new RpcMethod<RouteRefreshLeaseRequest, RouteRefreshLeaseReply>(ServiceId, RefreshRouteLeaseMethodId);

        public static readonly RpcMethod<RouteExpireRequest, RouteExpireReply> ExpireRoutesMethod =
            new RpcMethod<RouteExpireRequest, RouteExpireReply>(ServiceId, ExpireRoutesMethodId);

        public static readonly RpcMethod<RouteClearByNodeRequest, RouteClearReply> ClearRoutesByNodeMethod =
            new RpcMethod<RouteClearByNodeRequest, RouteClearReply>(ServiceId, ClearRoutesByNodeMethodId);

        public static readonly RpcMethod<RouteClearByNodeEpochRequest, RouteClearReply> ClearRoutesByNodeEpochMethod =
            new RpcMethod<RouteClearByNodeEpochRequest, RouteClearReply>(ServiceId, ClearRoutesByNodeEpochMethodId);

        public static readonly RpcMethod<NodeRegisterRequest, NodeRegisterReply> RegisterNodeMethod =
            new RpcMethod<NodeRegisterRequest, NodeRegisterReply>(ServiceId, RegisterNodeMethodId);

        public static readonly RpcMethod<NodeHeartbeatRequest, NodeHeartbeatReply> HeartbeatNodeMethod =
            new RpcMethod<NodeHeartbeatRequest, NodeHeartbeatReply>(ServiceId, HeartbeatNodeMethodId);

        public static readonly RpcMethod<NodeUpdateStateRequest, NodeUpdateStateReply> UpdateNodeStateMethod =
            new RpcMethod<NodeUpdateStateRequest, NodeUpdateStateReply>(ServiceId, UpdateNodeStateMethodId);

        public static readonly RpcMethod<NodeResolveRequest, NodeResolveReply> ResolveNodeMethod =
            new RpcMethod<NodeResolveRequest, NodeResolveReply>(ServiceId, ResolveNodeMethodId);

        public static readonly RpcMethod<NodeQueryRequest, NodeQueryReply> QueryNodesMethod =
            new RpcMethod<NodeQueryRequest, NodeQueryReply>(ServiceId, QueryNodesMethodId);

        public static readonly RpcMethod<NodeExpireRequest, NodeExpireReply> ExpireNodesMethod =
            new RpcMethod<NodeExpireRequest, NodeExpireReply>(ServiceId, ExpireNodesMethodId);
    }
}
