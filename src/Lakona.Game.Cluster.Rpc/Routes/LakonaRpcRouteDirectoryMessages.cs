using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcRouteLocationDto
    {
        public string Route { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public string EndpointAddress { get; set; } = string.Empty;

        public Dictionary<string, string>? EndpointMetadata { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public long NodeEpoch { get; set; }

        public long Generation { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class ULinkRpcRouteRegisterRequest
    {
        public ULinkRpcRouteLocationDto? Location { get; set; }
    }

    public sealed class ULinkRpcRouteRegisterReply
    {
        public int Status { get; set; }
    }

    public sealed class ULinkRpcRouteResolveRequest
    {
        public string Route { get; set; } = string.Empty;

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcRouteResolveReply
    {
        public ULinkRpcRouteLocationDto? Location { get; set; }
    }

    public sealed class ULinkRpcRouteRefreshLeaseRequest
    {
        public ULinkRpcRouteLocationDto? ExpectedLocation { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcRouteRefreshLeaseReply
    {
        public int Status { get; set; }
    }

    public sealed class ULinkRpcRouteExpireRequest
    {
        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcRouteExpireReply
    {
        public int Removed { get; set; }
    }

    public sealed class ULinkRpcRouteClearByNodeRequest
    {
        public string Node { get; set; } = string.Empty;
    }

    public sealed class ULinkRpcRouteClearByNodeEpochRequest
    {
        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }
    }

    public sealed class ULinkRpcRouteClearReply
    {
        public int Removed { get; set; }
    }
}
