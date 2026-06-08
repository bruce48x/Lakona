using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class NodeEndpointDto
    {
        public string Address { get; set; } = string.Empty;

        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class NodeServiceDto
    {
        public string Kind { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class NodeRegistrationDto
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public Dictionary<string, NodeEndpointDto>? Endpoints { get; set; }

        public List<NodeServiceDto>? Services { get; set; }

        public Dictionary<string, string>? Labels { get; set; }

        public int State { get; set; }

        public DateTimeOffset LeaseExpiresAt { get; set; }
    }

    public sealed class NodeRecordDto
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }

        public Dictionary<string, NodeEndpointDto>? Endpoints { get; set; }

        public List<NodeServiceDto>? Services { get; set; }

        public Dictionary<string, string>? Labels { get; set; }

        public int State { get; set; }

        public DateTimeOffset LeaseExpiresAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class NodeDirectoryClientQueryDto
    {
        public string ClusterName { get; set; } = string.Empty;

        public string? ServiceKind { get; set; }

        public string? ServiceName { get; set; }

        public int? State { get; set; }

        public Dictionary<string, string>? Labels { get; set; }

        public bool IncludeExpired { get; set; }
    }

    public sealed class NodeRegisterRequest
    {
        public NodeRegistrationDto? Registration { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class NodeRegisterReply
    {
        public int Status { get; set; }

        public NodeRecordDto? Record { get; set; }
    }

    public sealed class NodeHeartbeatRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }

        public DateTimeOffset LeaseExpiresAt { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class NodeHeartbeatReply
    {
        public int Status { get; set; }
    }

    public sealed class NodeUpdateStateRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public long NodeEpoch { get; set; }

        public int State { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class NodeUpdateStateReply
    {
        public int Status { get; set; }
    }

    public sealed class NodeResolveRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public string Node { get; set; } = string.Empty;

        public DateTimeOffset Now { get; set; }
    }

    public sealed class NodeResolveReply
    {
        public NodeRecordDto? Record { get; set; }
    }

    public sealed class NodeQueryRequest
    {
        public NodeDirectoryClientQueryDto? Query { get; set; }

        public DateTimeOffset Now { get; set; }
    }

    public sealed class NodeQueryReply
    {
        public List<NodeRecordDto>? Records { get; set; }
    }

    public sealed class NodeExpireRequest
    {
        public string ClusterName { get; set; } = string.Empty;

        public DateTimeOffset Now { get; set; }
    }

    public sealed class NodeExpireReply
    {
        public int Expired { get; set; }
    }
}
