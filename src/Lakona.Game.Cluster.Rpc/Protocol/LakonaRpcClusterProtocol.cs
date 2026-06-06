using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ULinkRpcClusterProtocol
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

        public static readonly RpcMethod<ULinkRpcClusterSendRequest, ULinkRpcClusterSendReply> SendMethod =
            new RpcMethod<ULinkRpcClusterSendRequest, ULinkRpcClusterSendReply>(ServiceId, SendMethodId);

        public static readonly RpcMethod<ULinkRpcRouteRegisterRequest, ULinkRpcRouteRegisterReply> RegisterRouteMethod =
            new RpcMethod<ULinkRpcRouteRegisterRequest, ULinkRpcRouteRegisterReply>(ServiceId, RegisterRouteMethodId);

        public static readonly RpcMethod<ULinkRpcRouteResolveRequest, ULinkRpcRouteResolveReply> ResolveRouteMethod =
            new RpcMethod<ULinkRpcRouteResolveRequest, ULinkRpcRouteResolveReply>(ServiceId, ResolveRouteMethodId);

        public static readonly RpcMethod<ULinkRpcRouteRefreshLeaseRequest, ULinkRpcRouteRefreshLeaseReply> RefreshRouteLeaseMethod =
            new RpcMethod<ULinkRpcRouteRefreshLeaseRequest, ULinkRpcRouteRefreshLeaseReply>(ServiceId, RefreshRouteLeaseMethodId);

        public static readonly RpcMethod<ULinkRpcRouteExpireRequest, ULinkRpcRouteExpireReply> ExpireRoutesMethod =
            new RpcMethod<ULinkRpcRouteExpireRequest, ULinkRpcRouteExpireReply>(ServiceId, ExpireRoutesMethodId);

        public static readonly RpcMethod<ULinkRpcRouteClearByNodeRequest, ULinkRpcRouteClearReply> ClearRoutesByNodeMethod =
            new RpcMethod<ULinkRpcRouteClearByNodeRequest, ULinkRpcRouteClearReply>(ServiceId, ClearRoutesByNodeMethodId);

        public static readonly RpcMethod<ULinkRpcRouteClearByNodeEpochRequest, ULinkRpcRouteClearReply> ClearRoutesByNodeEpochMethod =
            new RpcMethod<ULinkRpcRouteClearByNodeEpochRequest, ULinkRpcRouteClearReply>(ServiceId, ClearRoutesByNodeEpochMethodId);

        public static readonly RpcMethod<ULinkRpcNodeRegisterRequest, ULinkRpcNodeRegisterReply> RegisterNodeMethod =
            new RpcMethod<ULinkRpcNodeRegisterRequest, ULinkRpcNodeRegisterReply>(ServiceId, RegisterNodeMethodId);

        public static readonly RpcMethod<ULinkRpcNodeHeartbeatRequest, ULinkRpcNodeHeartbeatReply> HeartbeatNodeMethod =
            new RpcMethod<ULinkRpcNodeHeartbeatRequest, ULinkRpcNodeHeartbeatReply>(ServiceId, HeartbeatNodeMethodId);

        public static readonly RpcMethod<ULinkRpcNodeUpdateStateRequest, ULinkRpcNodeUpdateStateReply> UpdateNodeStateMethod =
            new RpcMethod<ULinkRpcNodeUpdateStateRequest, ULinkRpcNodeUpdateStateReply>(ServiceId, UpdateNodeStateMethodId);

        public static readonly RpcMethod<ULinkRpcNodeResolveRequest, ULinkRpcNodeResolveReply> ResolveNodeMethod =
            new RpcMethod<ULinkRpcNodeResolveRequest, ULinkRpcNodeResolveReply>(ServiceId, ResolveNodeMethodId);

        public static readonly RpcMethod<ULinkRpcNodeQueryRequest, ULinkRpcNodeQueryReply> QueryNodesMethod =
            new RpcMethod<ULinkRpcNodeQueryRequest, ULinkRpcNodeQueryReply>(ServiceId, QueryNodesMethodId);

        public static readonly RpcMethod<ULinkRpcNodeExpireRequest, ULinkRpcNodeExpireReply> ExpireNodesMethod =
            new RpcMethod<ULinkRpcNodeExpireRequest, ULinkRpcNodeExpireReply>(ServiceId, ExpireNodesMethodId);
    }
}
