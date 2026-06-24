using System;
using System.Collections.Generic;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteLocationDto
    {
        [MemoryPackOrder(0)]
        public string Route { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public string EndpointAddress { get; set; } = string.Empty;

        [MemoryPackOrder(3)]
        public Dictionary<string, string>? EndpointMetadata { get; set; }

        [MemoryPackOrder(4)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(5)]
        public long NodeEpoch { get; set; }

        [MemoryPackOrder(6)]
        public long Generation { get; set; }

        [MemoryPackOrder(7)]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteRegisterRequest
    {
        [MemoryPackOrder(0)]
        public RouteLocationDto? Location { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteRegisterReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteResolveRequest
    {
        [MemoryPackOrder(0)]
        public string Route { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteResolveReply
    {
        [MemoryPackOrder(0)]
        public RouteLocationDto? Location { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteUnregisterRequest
    {
        [MemoryPackOrder(0)]
        public string Route { get; set; } = string.Empty;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteUnregisterReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteRefreshLeaseRequest
    {
        [MemoryPackOrder(0)]
        public RouteLocationDto? ExpectedLocation { get; set; }

        [MemoryPackOrder(1)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(2)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteRefreshLeaseReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteExpireRequest
    {
        [MemoryPackOrder(0)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteExpireReply
    {
        [MemoryPackOrder(0)]
        public int Removed { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteClearByNodeRequest
    {
        [MemoryPackOrder(0)]
        public string Node { get; set; } = string.Empty;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteClearByNodeEpochRequest
    {
        [MemoryPackOrder(0)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public long NodeEpoch { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RouteClearReply
    {
        [MemoryPackOrder(0)]
        public int Removed { get; set; }
    }
}
