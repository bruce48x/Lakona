using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

internal static class ActorLifecycleProtocol
{
    public const int CreateMethodId = 25;
    public static readonly RpcMethod<ActorLifecycleRequest, ActorLifecycleReply> Create =
        new(ClusterProtocol.ServiceId, CreateMethodId);
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLifecycleRequest
{
    [MemoryPackOrder(0)] public string Actor { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public string ActorId { get; set; } = string.Empty;
    [MemoryPackOrder(2)] public string Mode { get; set; } = string.Empty;
    [MemoryPackOrder(3)] public string BuildTag { get; set; } = string.Empty;
    [MemoryPackOrder(4)] public Guid ClusterIncarnation { get; set; }
    [MemoryPackOrder(5)] public Guid NodeIncarnation { get; set; }
    [MemoryPackOrder(6)] public Guid ActivationId { get; set; }
    [MemoryPackOrder(7)] public long ActivationVersion { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLifecycleReply
{
    [MemoryPackOrder(0)] public bool Succeeded { get; set; }
    [MemoryPackOrder(1)] public string? OwnerNode { get; set; }
    [MemoryPackOrder(2)] public string Message { get; set; } = string.Empty;
}
