using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

internal static class ActorDirectoryProtocol
{
    public static readonly RpcMethod<ActorDirectoryRequest, ActorDirectoryReply> Lookup =
        new(ClusterProtocol.ServiceId, ClusterProtocol.Methods.ActorDirectoryLookup);
    public static readonly RpcMethod<ActorDirectoryRequest, ActorDirectoryReply> Acquire =
        new(ClusterProtocol.ServiceId, ClusterProtocol.Methods.ActorDirectoryAcquire);
    public static readonly RpcMethod<ActorDirectoryRequest, ActorDirectoryReply> Release =
        new(ClusterProtocol.ServiceId, ClusterProtocol.Methods.ActorDirectoryRelease);
    public static readonly RpcMethod<ActorDirectoryActivationSnapshotRequest, ActorDirectorySnapshotReply>
        ActivationSnapshot = new(
            ClusterProtocol.ServiceId,
            ClusterProtocol.Methods.ActorDirectoryActivationSnapshot);
    public static readonly RpcMethod<ActorDirectoryPartitionSnapshotRequest, ActorDirectorySnapshotReply>
        PartitionSnapshot = new(
            ClusterProtocol.ServiceId,
            ClusterProtocol.Methods.ActorDirectorySnapshot);
    public static readonly RpcMethod<ActorDirectorySnapshotAcknowledgeRequest, ActorDirectoryAcknowledgeReply>
        AcknowledgeSnapshot = new(
            ClusterProtocol.ServiceId,
            ClusterProtocol.Methods.ActorDirectorySnapshotAcknowledge);
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryRequest
{
    [MemoryPackOrder(0)] public string ActorId { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public int PartitionIndex { get; set; }
    [MemoryPackOrder(3)] public Guid HostCluster { get; set; }
    [MemoryPackOrder(4)] public string HostNode { get; set; } = string.Empty;
    [MemoryPackOrder(5)] public Guid HostIncarnation { get; set; }
    [MemoryPackOrder(6)] public Guid Activation { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryReply
{
    [MemoryPackOrder(0)] public int Status { get; set; }
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public ActorDirectoryRecordDto? Record { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryRecordDto
{
    [MemoryPackOrder(0)] public string ActorId { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public Guid HostCluster { get; set; }
    [MemoryPackOrder(2)] public string HostNode { get; set; } = string.Empty;
    [MemoryPackOrder(3)] public Guid HostIncarnation { get; set; }
    [MemoryPackOrder(4)] public Guid Activation { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryRangeDto
{
    [MemoryPackOrder(0)] public uint Start { get; set; }
    [MemoryPackOrder(1)] public uint End { get; set; }
    [MemoryPackOrder(2)] public int Kind { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryPartitionSnapshotRequest
{
    [MemoryPackOrder(0)] public long View { get; set; }
    [MemoryPackOrder(1)] public long SnapshotView { get; set; }
    [MemoryPackOrder(2)] public int PartitionIndex { get; set; }
    [MemoryPackOrder(3)] public ActorDirectoryRangeDto Range { get; set; } = new();
    [MemoryPackOrder(4)] public int Offset { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryActivationSnapshotRequest
{
    [MemoryPackOrder(0)] public long View { get; set; }
    [MemoryPackOrder(1)] public ActorDirectoryRangeDto Range { get; set; } = new();
    [MemoryPackOrder(2)] public int Offset { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectorySnapshotReply
{
    [MemoryPackOrder(0)] public bool Available { get; set; }
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public IReadOnlyList<ActorDirectoryRecordDto> Records { get; set; } = [];
    [MemoryPackOrder(3)] public bool HasMore { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectorySnapshotAcknowledgeRequest
{
    [MemoryPackOrder(0)] public long View { get; set; }
    [MemoryPackOrder(1)] public long SnapshotView { get; set; }
    [MemoryPackOrder(2)] public int PartitionIndex { get; set; }
    [MemoryPackOrder(3)] public Guid ReceiverCluster { get; set; }
    [MemoryPackOrder(4)] public string ReceiverNode { get; set; } = string.Empty;
    [MemoryPackOrder(5)] public Guid ReceiverIncarnation { get; set; }
    [MemoryPackOrder(6)] public int ReceiverPartitionIndex { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorDirectoryAcknowledgeReply
{
    [MemoryPackOrder(0)] public bool Applied { get; set; }
    [MemoryPackOrder(1)] public long View { get; set; }
}
