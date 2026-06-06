using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcNodeEndpointDto
    {
        public string Address { get; set; } = string.Empty;

        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class ULinkRpcNodeServiceDto
    {
        public string Kind { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class ULinkRpcNodeRegistrationDto
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public Dictionary<string, ULinkRpcNodeEndpointDto>? Endpoints { get; set; }

        public List<ULinkRpcNodeServiceDto>? Services { get; set; }

        public Dictionary<string, string>? Labels { get; set; }

        public int State { get; set; }

        public DateTimeOffset LeaseExpiresAt { get; set; }
    }

    public sealed class ULinkRpcNodeRecordDto
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }

        public Dictionary<string, ULinkRpcNodeEndpointDto>? Endpoints { get; set; }

        public List<ULinkRpcNodeServiceDto>? Services { get; set; }

        public Dictionary<string, string>? Labels { get; set; }

        public int State { get; set; }

        public DateTimeOffset LeaseExpiresAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class ULinkRpcNodeDirectoryQueryDto
    {
        public string ClusterName { get; set; } = string.Empty;

        public string? ServiceKind { get; set; }

        public string? ServiceName { get; set; }

        public int? State { get; set; }

        public Dictionary<string, string>? Labels { get; set; }

        public bool IncludeExpired { get; set; }
    }

    public sealed class ULinkRpcNodeRegisterRequest
    {
        public ULinkRpcNodeRegistrationDto? Registration { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcNodeRegisterReply
    {
        public int Status { get; set; }

        public ULinkRpcNodeRecordDto? Record { get; set; }
    }

    public sealed class ULinkRpcNodeHeartbeatRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }

        public DateTimeOffset LeaseExpiresAt { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcNodeHeartbeatReply
    {
        public int Status { get; set; }
    }

    public sealed class ULinkRpcNodeUpdateStateRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }

        public int State { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcNodeUpdateStateReply
    {
        public int Status { get; set; }
    }

    public sealed class ULinkRpcNodeResolveRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcNodeResolveReply
    {
        public ULinkRpcNodeRecordDto? Record { get; set; }
    }

    public sealed class ULinkRpcNodeQueryRequest
    {
        public ULinkRpcNodeDirectoryQueryDto? Query { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcNodeQueryReply
    {
        public List<ULinkRpcNodeRecordDto>? Records { get; set; }
    }

    public sealed class ULinkRpcNodeExpireRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public DateTimeOffset Now { get; set; }
    }

    public sealed class ULinkRpcNodeExpireReply
    {
        public int Expired { get; set; }
    }
}
