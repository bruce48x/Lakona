using Lakona.Game.Server.Actors;
using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

internal static class ActorLifecycleProtocol
{
    public static int CreateMethodId => ClusterProtocol.Methods.ActorLifecycleCreate;
    public static int DestroyMethodId => ClusterProtocol.Methods.ActorLifecycleDestroy;
    public static readonly RpcMethod<ActorLifecycleCreateRequest, ActorLifecycleReply> Create =
        new(ClusterProtocol.ServiceId, CreateMethodId);
    public static readonly RpcMethod<ActorLifecycleDestroyRequest, ActorLifecycleReply> Destroy =
        new(ClusterProtocol.ServiceId, DestroyMethodId);
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLifecycleCreateRequest
{
    [MemoryPackOrder(0)] public string Actor { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public ActorPlacementCreateMode Mode { get; set; }
    [MemoryPackOrder(2)] public string BuildTag { get; set; } = string.Empty;
    [MemoryPackOrder(3)] public ActorLifecycleWireTarget Target { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLifecycleDestroyRequest
{
    [MemoryPackOrder(0)] public string Actor { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public ActorLifecycleWireTarget Target { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLifecycleWireTarget
{
    [MemoryPackOrder(0)] public string ActorId { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public Guid ClusterIncarnation { get; set; }
    [MemoryPackOrder(2)] public string Node { get; set; } = string.Empty;
    [MemoryPackOrder(3)] public Guid NodeIncarnation { get; set; }
    [MemoryPackOrder(4)] public Guid ActivationId { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ActorLifecycleReply
{
    [MemoryPackOrder(0)] public bool Succeeded { get; set; }
    [MemoryPackOrder(1)] public string? OwnerNode { get; set; }
    [MemoryPackOrder(2)] public string Message { get; set; } = string.Empty;
}

internal static class ActorLifecycleWireRequest
{
    public static ActorLifecycleCreateRequest From(ActorHostCreateCommand command) => new()
    {
        Actor = command.Actor,
        Mode = command.Mode,
        BuildTag = command.BuildTag,
        Target = From(command.Target)
    };

    public static ActorLifecycleDestroyRequest From(ActorHostDestroyCommand command) => new()
    {
        Actor = command.Actor,
        Target = From(command.Target)
    };

    public static ActorLifecycleTarget DecodeTarget(ActorLifecycleWireTarget? target)
    {
        if (target is null
            || string.IsNullOrWhiteSpace(target.ActorId)
            || target.ClusterIncarnation == Guid.Empty
            || string.IsNullOrWhiteSpace(target.Node)
            || target.NodeIncarnation == Guid.Empty
            || target.ActivationId == Guid.Empty)
        {
            throw new InvalidOperationException("Actor lifecycle target is invalid.");
        }

        return new ActorLifecycleTarget(
            ActorId.From(target.ActorId),
            new NodeReference(
                new ClusterIncarnationId(target.ClusterIncarnation),
                new NodeId(target.Node),
                new NodeIncarnationId(target.NodeIncarnation)),
            new ActorActivationId(target.ActivationId));
    }

    private static ActorLifecycleWireTarget From(ActorLifecycleTarget target) => new()
    {
        ActorId = target.ActorId.Value,
        ClusterIncarnation = target.Owner.Cluster.Value,
        Node = target.Owner.Node.Value,
        NodeIncarnation = target.Owner.Incarnation.Value,
        ActivationId = target.ActivationId.Value
    };
}
