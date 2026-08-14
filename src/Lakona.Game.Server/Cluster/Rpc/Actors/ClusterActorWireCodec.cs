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

    [MemoryPackOrder(3)]
    public Guid? TargetClusterIncarnation { get; set; }

    [MemoryPackOrder(4)]
    public string? TargetNode { get; set; }

    [MemoryPackOrder(5)]
    public Guid? TargetNodeIncarnation { get; set; }

    [MemoryPackOrder(6)]
    public long? TargetMembershipView { get; set; }

    [MemoryPackOrder(7)]
    public long? ReservedLegacyNodeEpoch { get; set; }

    [MemoryPackOrder(8)]
    public Guid? ActivationId { get; set; }
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
    ReadOnlyMemory<byte> Body);

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
        var header = new ClusterActorWireRequestHeader
        {
            ActorId = invocation.ActorId.Value,
            MethodId = invocation.MethodId,
            Deadline = invocation.Deadline,
            TargetClusterIncarnation = targetReference.Cluster.Value,
            TargetNode = targetReference.Node.Value,
            TargetNodeIncarnation = targetReference.Incarnation.Value,
            TargetMembershipView = target.MembershipView.Value,
            ReservedLegacyNodeEpoch = null,
            ActivationId = invocation.ActivationId?.Value
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

        return new ClusterActorWireRequest(header, payload.Slice(consumed));
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
}
