using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

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
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class MembershipProbeReply
{
    [MemoryPackOrder(0)] public bool IsAlive { get; set; }
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
