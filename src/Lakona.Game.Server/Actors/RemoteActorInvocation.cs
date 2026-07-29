using System.Buffers;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvocation
{
    private readonly IRemoteActorCallCodec codec;
    private readonly object? request;

    private RemoteActorInvocation(
        NodeId node,
        ActorId actorId,
        string actorName,
        string methodName,
        ulong methodId,
        object? request,
        IRemoteActorCallCodec codec,
        DateTimeOffset deadline,
        long? expectedNodeEpoch,
        NodeReference? ownerReference,
        ActorActivationId? activationId,
        long activationVersion)
    {
        Node = node;
        ActorId = actorId;
        ActorName = actorName;
        MethodName = methodName;
        MethodId = methodId;
        this.request = request;
        this.codec = codec;
        Deadline = deadline;
        ExpectedNodeEpoch = expectedNodeEpoch;
        OwnerReference = ownerReference;
        ActivationId = activationId;
        ActivationVersion = activationVersion;
    }

    public NodeId Node { get; }

    public ActorId ActorId { get; }

    public string ActorName { get; }

    public string MethodName { get; }

    public ulong MethodId { get; }

    public DateTimeOffset Deadline { get; }

    public long? ExpectedNodeEpoch { get; }

    public NodeReference? OwnerReference { get; }

    public ActorActivationId? ActivationId { get; }

    public long ActivationVersion { get; }

    public static RemoteActorInvocation Create<TRequest>(
        NodeId node,
        ActorId actorId,
        string actorName,
        string methodName,
        ulong methodId,
        TRequest request,
        DateTimeOffset deadline,
        long? expectedNodeEpoch = null,
        NodeReference? ownerReference = null,
        ActorActivationId? activationId = null,
        long activationVersion = 0)
    {
        return new RemoteActorInvocation(
            node,
            actorId,
            actorName,
            methodName,
            methodId,
            request,
            RemoteActorCallCodec<TRequest>.Instance,
            deadline,
            expectedNodeEpoch,
            ownerReference,
            activationId,
            activationVersion);
    }

    public static RemoteActorInvocation Create<TRequest, TResult>(
        NodeId node,
        ActorId actorId,
        string actorName,
        string methodName,
        ulong methodId,
        TRequest request,
        DateTimeOffset deadline,
        long? expectedNodeEpoch = null,
        NodeReference? ownerReference = null,
        ActorActivationId? activationId = null,
        long activationVersion = 0)
    {
        return new RemoteActorInvocation(
            node,
            actorId,
            actorName,
            methodName,
            methodId,
            request,
            RemoteActorCallCodec<TRequest, TResult>.Instance,
            deadline,
            expectedNodeEpoch,
            ownerReference,
            activationId,
            activationVersion);
    }

    internal void SerializeRequest(IBufferWriter<byte> writer)
    {
        codec.SerializeRequest(writer, request);
    }

    internal object? DeserializeReply(ReadOnlyMemory<byte> payload)
    {
        return codec.DeserializeReply(payload);
    }

    internal TRequest GetRequest<TRequest>()
    {
        return request is TRequest typed
            ? typed
            : throw new InvalidOperationException(
                $"Remote Actor request is '{request?.GetType().FullName ?? "null"}', not '{typeof(TRequest).FullName}'.");
    }

    internal RemoteActorInvocation WithActivation(ActorDirectoryRecord record)
    {
        return new RemoteActorInvocation(
            Node,
            ActorId,
            ActorName,
            MethodName,
            MethodId,
            request,
            codec,
            Deadline,
            ExpectedNodeEpoch,
            record.OwnerReference,
            record.ActivationId,
            record.Version);
    }

    private interface IRemoteActorCallCodec
    {
        void SerializeRequest(IBufferWriter<byte> writer, object? value);

        object? DeserializeReply(ReadOnlyMemory<byte> payload);
    }

    private sealed class RemoteActorCallCodec<TRequest> : IRemoteActorCallCodec
    {
        public static RemoteActorCallCodec<TRequest> Instance { get; } = new();

        public void SerializeRequest(IBufferWriter<byte> writer, object? value)
        {
            var requestValue = (TRequest)value!;
            MemoryPackSerializer.Serialize(writer, requestValue);
        }

        public object? DeserializeReply(ReadOnlyMemory<byte> payload)
        {
            if (!payload.IsEmpty)
            {
                throw new InvalidOperationException(
                    "A resultless remote Actor invocation returned a payload.");
            }

            return null;
        }
    }

    private sealed class RemoteActorCallCodec<TRequest, TResult> : IRemoteActorCallCodec
    {
        public static RemoteActorCallCodec<TRequest, TResult> Instance { get; } = new();

        public void SerializeRequest(IBufferWriter<byte> writer, object? value)
        {
            var requestValue = (TRequest)value!;
            MemoryPackSerializer.Serialize(writer, requestValue);
        }

        public object? DeserializeReply(ReadOnlyMemory<byte> payload)
        {
            return MemoryPackSerializer.Deserialize<TResult>(payload.Span);
        }
    }
}
