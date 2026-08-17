using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

internal static class ActorLocationProtocol
{
    public static int LookupMethodId => ClusterProtocol.Methods.ActorLocationLookup.Id;
    public static int RegisterMethodId => ClusterProtocol.Methods.ActorLocationRegister.Id;
    public static int UnregisterMethodId => ClusterProtocol.Methods.ActorLocationUnregister.Id;
    public static int RegistrySnapshotMethodId => ClusterProtocol.Methods.ActorLocationRegistrySnapshot.Id;

    public static readonly RpcMethod<ActorLocationRequest, ActorLocationReply> Lookup =
        new(ClusterProtocol.ServiceId, LookupMethodId);
    public static readonly RpcMethod<ActorLocationRequest, ActorLocationReply> Register =
        new(ClusterProtocol.ServiceId, RegisterMethodId);
    public static readonly RpcMethod<ActorLocationRequest, ActorLocationReply> Unregister =
        new(ClusterProtocol.ServiceId, UnregisterMethodId);
    public static readonly RpcMethod<ActorRegistrySnapshotRequest, ActorRegistrySnapshotReply> RegistrySnapshot =
        new(ClusterProtocol.ServiceId, RegistrySnapshotMethodId);
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorRegistrySnapshotRequest
{
    [MemoryPackOrder(0)] public int Shard { get; set; }
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public int Offset { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorRegistrySnapshotReply
{
    [MemoryPackOrder(0)] public IReadOnlyList<ActorLocationRecordDto> Records { get; set; } = [];
    [MemoryPackOrder(1)] public bool HasMore { get; set; }
    [MemoryPackOrder(2)] public bool RecoveryEligible { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLocationRecordDto
{
    [MemoryPackOrder(0)] public string ActorId { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public Guid HostCluster { get; set; }
    [MemoryPackOrder(2)] public string HostNode { get; set; } = string.Empty;
    [MemoryPackOrder(3)] public Guid HostIncarnation { get; set; }
    [MemoryPackOrder(4)] public Guid Activation { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLocationRequest
{
    [MemoryPackOrder(0)] public string ActorId { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public Guid HostCluster { get; set; }
    [MemoryPackOrder(3)] public string HostNode { get; set; } = string.Empty;
    [MemoryPackOrder(4)] public Guid HostIncarnation { get; set; }
    [MemoryPackOrder(5)] public Guid Activation { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLocationReply
{
    [MemoryPackOrder(0)] public int Status { get; set; }
    [MemoryPackOrder(1)] public long View { get; set; }
    [MemoryPackOrder(2)] public Guid OwnerCluster { get; set; }
    [MemoryPackOrder(3)] public string OwnerNode { get; set; } = string.Empty;
    [MemoryPackOrder(4)] public Guid OwnerIncarnation { get; set; }
    [MemoryPackOrder(5)] public Guid HostCluster { get; set; }
    [MemoryPackOrder(6)] public string HostNode { get; set; } = string.Empty;
    [MemoryPackOrder(7)] public Guid HostIncarnation { get; set; }
    [MemoryPackOrder(8)] public Guid Activation { get; set; }
}
