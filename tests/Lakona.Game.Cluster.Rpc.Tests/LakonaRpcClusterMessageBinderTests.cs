using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ULinkRpcClusterMessageBinderTests
{
    [Fact]
    public async Task BinderRegistersClusterSendHandlerAndReturnsDispatchStatus()
    {
        var registry = new RpcServiceRegistry();
        var handler = new RecordingHandler(ClusterSendStatus.Accepted);
        ULinkRpcClusterMessageBinder.Bind(registry, handler);
        var found = registry.TryGetHandler(
            ULinkRpcClusterProtocol.ServiceId,
            ULinkRpcClusterProtocol.SendMethodId,
            out var rpcHandler);

        Assert.True(found);
        Assert.NotNull(rpcHandler);

        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        using var payload = serializer.SerializeFrame(new ULinkRpcClusterSendRequest
        {
            Route = "room/1",
            Kind = "command",
            Payload = new byte[] { 1, 2, 3 },
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            SourceNode = "source",
            CorrelationId = "corr-1",
            TraceId = "trace-1",
            OrderedBy = "room/1"
        });
        using var frame = await rpcHandler(
            session,
            new RpcRequestFrame(
                1,
                ULinkRpcClusterProtocol.ServiceId,
                ULinkRpcClusterProtocol.SendMethodId,
                payload),
            TestContext.Current.CancellationToken);

        using var response = RpcEnvelopeCodec.DecodeResponse(frame);
        var reply = serializer.Deserialize<ULinkRpcClusterSendReply>(response.Payload.Memory);
        var message = Assert.Single(handler.Messages);
        Assert.Equal((uint)1, response.RequestId);
        Assert.Equal(RpcStatus.Ok, response.Status);
        Assert.Equal(ClusterSendStatus.Accepted, (ClusterSendStatus)reply.Status);
        Assert.Equal(new RouteKey("room/1"), message.Route);
        Assert.Equal("command", message.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, message.Payload.ToArray());
        Assert.Equal(new NodeId("source"), message.SourceNode);
        Assert.Equal("corr-1", message.CorrelationId);
    }

    private sealed class RecordingHandler : IClusterMessageHandler
    {
        private readonly ClusterSendStatus _status;

        public RecordingHandler(ClusterSendStatus status)
        {
            _status = status;
        }

        public List<ClusterMessage> Messages { get; } = new();

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return ValueTask.FromResult(_status);
        }
    }

    private sealed class JsonTestSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value)
        {
            return TransportFrame.CopyOf(JsonSerializer.SerializeToUtf8Bytes(value));
        }

        public T Deserialize<T>(ReadOnlySpan<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload)!;
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return Deserialize<T>(payload.Span);
        }
    }

    private sealed class FakeTransport : ITransport
    {
        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            return default;
        }

        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }
}
