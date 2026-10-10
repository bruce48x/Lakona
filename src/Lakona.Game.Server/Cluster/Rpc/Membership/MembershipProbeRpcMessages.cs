using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

// Unknown is the safe default: no valid target observation was obtained.
internal enum MembershipProbeStatus : byte
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class MembershipProbeRequest
{
    [MemoryPackOrder(0)] public Guid Cluster { get; set; }
    [MemoryPackOrder(1)] public string SourceNodeId { get; set; } = "";
    [MemoryPackOrder(2)] public Guid SourceIncarnation { get; set; }
    [MemoryPackOrder(3)] public string TargetNodeId { get; set; } = "";
    [MemoryPackOrder(4)] public Guid TargetIncarnation { get; set; }
    [MemoryPackOrder(5)] public string TargetEndpoint { get; set; } = "";
    [MemoryPackOrder(6)] public bool Forward { get; set; }
    [MemoryPackOrder(7)] public TimeSpan TargetProbeTimeout { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class MembershipProbeReply
{
    [MemoryPackOrder(0)] public MembershipProbeStatus Status { get; set; }
    [MemoryPackOrder(1)] public long MembershipVersion { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class MembershipGossipRequest
{
    [MemoryPackOrder(0)] public Guid Cluster { get; set; }
    [MemoryPackOrder(1)] public string SourceNodeId { get; set; } = "";
    [MemoryPackOrder(2)] public Guid SourceIncarnation { get; set; }
    [MemoryPackOrder(3)] public long MembershipVersion { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class MembershipGossipReply;
