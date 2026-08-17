using System.Buffers;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Server.Actors;

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClusterActorWireRequestHeader
{
    [MemoryPackOrder(0)]
    public string ActorId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public ulong MethodId { get; set; }

    [MemoryPackOrder(2)]
    public DateTimeOffset Deadline { get; set; }

    // Orders 3-8 are tombstones for the former flat, nullable target proof.
    [MemoryPackOrder(9)]
    public ClusterActorWireTargetProof TargetProof { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClusterActorWireTargetProof
{
    [MemoryPackOrder(0)]
    public Guid ClusterIncarnation { get; set; }

    [MemoryPackOrder(1)]
    public string Node { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public Guid NodeIncarnation { get; set; }

    [MemoryPackOrder(3)]
    public long MembershipView { get; set; }

    [MemoryPackOrder(4)]
    public Guid ActivationId { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClusterActorWireReplyHeader
{
    [MemoryPackOrder(0)]
    public int Status { get; set; }

    [MemoryPackOrder(1)]
    public string? Message { get; set; }

    [MemoryPackOrder(2)]
    public int RetrySafety { get; set; }
}

internal readonly record struct ClusterActorWireRequest(
    ClusterActorWireRequestHeader Header,
    ClusterActorTargetProof TargetProof,
    ReadOnlyMemory<byte> Body);

internal readonly record struct ClusterActorTargetProof(
    NodeReference Target,
    MembershipViewId MembershipView,
    ActorActivationId ActivationId);

internal readonly record struct ClusterActorWireReply(
    RemoteActorStatus Status,
    string? Message,
    RemoteActorRetrySafety RetrySafety,
    ReadOnlyMemory<byte> Body);

internal static class ClusterActorWireCodec
{
    public static TransportFrame EncodeRequest(
        RemoteActorInvocation invocation,
        RouteLocation target)
    {
        using var writer = new PooledFrameBufferWriter();
        WriteRequest(writer, invocation, target);
        return writer.DetachFrame();
    }

    public static void WriteRequest(
        IBufferWriter<byte> writer,
        RemoteActorInvocation invocation,
        RouteLocation target)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(target);

        var targetReference = target.NodeReference;
        var activationId = invocation.ActivationId
            ?? throw new InvalidOperationException(
                "Remote Actor requests require an exact activation id.");
        var header = new ClusterActorWireRequestHeader
        {
            ActorId = invocation.ActorId.Value,
            MethodId = invocation.MethodId,
            Deadline = invocation.Deadline,
            TargetProof = new ClusterActorWireTargetProof
            {
                ClusterIncarnation = targetReference.Cluster.Value,
                Node = targetReference.Node.Value,
                NodeIncarnation = targetReference.Incarnation.Value,
                MembershipView = target.MembershipView.Value,
                ActivationId = activationId.Value
            }
        };

        MemoryPackSerializer.Serialize(writer, header);
        invocation.SerializeRequest(writer);
    }

    public static ClusterActorWireRequest DecodeRequest(ReadOnlyMemory<byte> payload)
    {
        ClusterActorWireRequestHeader? header = null;
        var consumed = MemoryPackSerializer.Deserialize(payload.Span, ref header);
        if (header is null || consumed <= 0 || consumed > payload.Length)
        {
            throw new InvalidOperationException("Remote Actor request header is invalid.");
        }

        var proof = DecodeTargetProof(header.TargetProof);
        return new ClusterActorWireRequest(header, proof, payload.Slice(consumed));
    }

    public static TransportFrame EncodeReply(
        RemoteActorStatus status,
        string? message = null,
        RemoteActorRetrySafety retrySafety = RemoteActorRetrySafety.Indeterminate,
        Action<IBufferWriter<byte>>? writeBody = null)
    {
        using var writer = new PooledFrameBufferWriter();
        WriteReply(writer, status, message, retrySafety, writeBody);
        return writer.DetachFrame();
    }

    public static void WriteReply(
        IBufferWriter<byte> writer,
        RemoteActorStatus status,
        string? message = null,
        RemoteActorRetrySafety retrySafety = RemoteActorRetrySafety.Indeterminate,
        Action<IBufferWriter<byte>>? writeBody = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var header = new ClusterActorWireReplyHeader
        {
            Status = (int)status,
            Message = message,
            RetrySafety = (int)retrySafety
        };

        MemoryPackSerializer.Serialize(writer, header);
        writeBody?.Invoke(writer);
    }

    public static ClusterActorWireReply DecodeReply(ReadOnlyMemory<byte> payload)
    {
        ClusterActorWireReplyHeader? header = null;
        var consumed = MemoryPackSerializer.Deserialize(payload.Span, ref header);
        if (header is null
            || consumed <= 0
            || consumed > payload.Length
            || !Enum.IsDefined(typeof(RemoteActorStatus), header.Status)
            || !Enum.IsDefined(typeof(RemoteActorRetrySafety), header.RetrySafety))
        {
            throw new InvalidOperationException("Remote Actor reply header is invalid.");
        }

        return new ClusterActorWireReply(
            (RemoteActorStatus)header.Status,
            header.Message,
            (RemoteActorRetrySafety)header.RetrySafety,
            payload.Slice(consumed));
    }

    private static ClusterActorTargetProof DecodeTargetProof(
        ClusterActorWireTargetProof? proof)
    {
        if (proof is null
            || proof.ClusterIncarnation == Guid.Empty
            || string.IsNullOrWhiteSpace(proof.Node)
            || proof.NodeIncarnation == Guid.Empty
            || proof.MembershipView <= 0
            || proof.ActivationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Remote Actor target proof is invalid.");
        }

        return new ClusterActorTargetProof(
            new NodeReference(
                new ClusterIncarnationId(proof.ClusterIncarnation),
                new NodeId(proof.Node),
                new NodeIncarnationId(proof.NodeIncarnation)),
            new MembershipViewId(proof.MembershipView),
            new ActorActivationId(proof.ActivationId));
    }
}
