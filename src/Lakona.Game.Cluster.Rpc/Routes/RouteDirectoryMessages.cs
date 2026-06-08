using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class RouteLocationDto
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

    public sealed class RouteRegisterRequest
    {
        public RouteLocationDto? Location { get; set; }
    }

    public sealed class RouteRegisterReply
    {
        public int Status { get; set; }
    }

    public sealed class RouteResolveRequest
    {
        public string Route { get; set; } = string.Empty;

        public DateTimeOffset Now { get; set; }
    }

    public sealed class RouteResolveReply
    {
        public RouteLocationDto? Location { get; set; }
    }

    public sealed class RouteRefreshLeaseRequest
    {
        public RouteLocationDto? ExpectedLocation { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class RouteRefreshLeaseReply
    {
        public int Status { get; set; }
    }

    public sealed class RouteExpireRequest
    {
        public DateTimeOffset Now { get; set; }
    }

    public sealed class RouteExpireReply
    {
        public int Removed { get; set; }
    }

    public sealed class RouteClearByNodeRequest
    {
        public string Node { get; set; } = string.Empty;
    }

    public sealed class RouteClearByNodeEpochRequest
    {
        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }
    }

    public sealed class RouteClearReply
    {
        public int Removed { get; set; }
    }
}
