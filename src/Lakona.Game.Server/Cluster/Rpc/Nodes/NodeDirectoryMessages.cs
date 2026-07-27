using System;
using System.Collections.Generic;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeEndpointDto
    {
        [MemoryPackOrder(0)]
        public string Address { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeActorHostDto
    {
        [MemoryPackOrder(0)]
        public string Actor { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string PolicyHash { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public string BuildTag { get; set; } = string.Empty;

        [MemoryPackOrder(3)]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeRegistrationDto
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public Dictionary<string, NodeEndpointDto>? Endpoints { get; set; }

        [MemoryPackOrder(3)]
        public List<NodeActorHostDto>? ActorHosts { get; set; }

        [MemoryPackOrder(4)]
        public Dictionary<string, string>? Labels { get; set; }

        [MemoryPackOrder(5)]
        public int State { get; set; }

        [MemoryPackOrder(6)]
        public DateTimeOffset LeaseExpiresAt { get; set; }

        [MemoryPackOrder(7)]
        public List<StartupActorDto>? StartupActors { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeRecordDto
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public long NodeEpoch { get; set; }

        [MemoryPackOrder(3)]
        public Dictionary<string, NodeEndpointDto>? Endpoints { get; set; }

        [MemoryPackOrder(4)]
        public List<NodeActorHostDto>? ActorHosts { get; set; }

        [MemoryPackOrder(5)]
        public Dictionary<string, string>? Labels { get; set; }

        [MemoryPackOrder(6)]
        public int State { get; set; }

        [MemoryPackOrder(7)]
        public DateTimeOffset LeaseExpiresAt { get; set; }

        [MemoryPackOrder(8)]
        public DateTimeOffset UpdatedAt { get; set; }

        [MemoryPackOrder(9)]
        public List<StartupActorDto>? StartupActors { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeDirectoryClientQueryDto
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string? ActorHostName { get; set; }

        [MemoryPackOrder(2)]
        public string? ActorHostPolicyHash { get; set; }

        [MemoryPackOrder(3)]
        public int? State { get; set; }

        [MemoryPackOrder(4)]
        public Dictionary<string, string>? Labels { get; set; }

        [MemoryPackOrder(5)]
        public bool IncludeExpired { get; set; }

        [MemoryPackOrder(6)]
        public string? StartupActorName { get; set; }

        [MemoryPackOrder(7)]
        public string? StartupActorPolicyHash { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeRegisterRequest
    {
        [MemoryPackOrder(0)]
        public NodeRegistrationDto? Registration { get; set; }

        [MemoryPackOrder(1)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeRegisterReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }

        [MemoryPackOrder(1)]
        public NodeRecordDto? Record { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeHeartbeatRequest
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public long NodeEpoch { get; set; }

        [MemoryPackOrder(3)]
        public DateTimeOffset LeaseExpiresAt { get; set; }

        [MemoryPackOrder(4)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeHeartbeatReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeUpdateStateRequest
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public long NodeEpoch { get; set; }

        [MemoryPackOrder(3)]
        public int State { get; set; }

        [MemoryPackOrder(4)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeUpdateStateReply
    {
        [MemoryPackOrder(0)]
        public int Status { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeResolveRequest
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeResolveReply
    {
        [MemoryPackOrder(0)]
        public NodeRecordDto? Record { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeQueryRequest
    {
        [MemoryPackOrder(0)]
        public NodeDirectoryClientQueryDto? Query { get; set; }

        [MemoryPackOrder(1)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeQueryReply
    {
        [MemoryPackOrder(0)]
        public List<NodeRecordDto>? Records { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeExpireRequest
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NodeExpireReply
    {
        [MemoryPackOrder(0)]
        public int Expired { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class StartupActorDto
    {
        [MemoryPackOrder(0)]
        public string Actor { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string PolicyHash { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public string BuildTag { get; set; } = string.Empty;

        [MemoryPackOrder(3)]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
