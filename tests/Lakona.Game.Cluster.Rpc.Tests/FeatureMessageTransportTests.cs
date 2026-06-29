using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class FeatureMessageTransportTests
{
    [Fact]
    public async Task TransportSendsFeatureMessageRpcToTargetClusterEndpoint()
    {
        var client = new RecordingRpcClient(new FeatureSendReply
        {
            Status = (int)ClusterSendStatus.Accepted,
            Payload = new byte[] { 9 }
        });
        var factory = new RecordingClientFactory(client);
        var transport = new RpcFeatureMessageTransport(factory);
        var target = NewTarget();
        var request = NewRequest();

        var reply = await transport.SendAsync(
            target,
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
        Assert.Equal(new byte[] { 9 }, reply.Payload.ToArray());
        Assert.NotNull(client.Request);
        Assert.Equal(ClusterProtocol.ServiceId, client.ServiceId);
        Assert.Equal(ClusterProtocol.FeatureMessageMethodId, client.MethodId);
        Assert.Equal(new NodeId("data-1"), factory.Targets.Single().Node);
        Assert.Equal("tcp://10.0.0.1:21001", factory.Targets.Single().Endpoint.Address);
        Assert.Equal("matchmaking", client.Request.Feature);
        Assert.Equal("join", client.Request.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, client.Request.Payload);
        Assert.Equal("gateway-1", client.Request.SourceNode);
        Assert.Equal("corr-1", client.Request.CorrelationId);
    }

    [Fact]
    public async Task BinderDispatchesRpcFeatureMessageToHandler()
    {
        var registry = new RpcServiceRegistry();
        var handler = new RecordingFeatureHandler(new FeatureMessageReply(
            ClusterSendStatus.Accepted,
            new byte[] { 7 }));
        FeatureMessageBinder.Bind(registry, handler);
        var found = registry.TryGetHandler(
            ClusterProtocol.ServiceId,
            ClusterProtocol.FeatureMessageMethodId,
            out var rpcHandler);

        Assert.True(found);
        Assert.NotNull(rpcHandler);

        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        using var payload = serializer.SerializeFrame(new FeatureSendRequest
        {
            Feature = "matchmaking",
            Kind = "join",
            Payload = new byte[] { 1, 2, 3 },
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            SourceNode = "gateway-1",
            CorrelationId = "corr-1"
        });
        using var frame = await rpcHandler(
            session,
            new RpcRequestFrame(
                1,
                ClusterProtocol.ServiceId,
                ClusterProtocol.FeatureMessageMethodId,
                payload),
            TestContext.Current.CancellationToken);

        using var response = RpcEnvelopeCodec.DecodeResponse(frame);
        var reply = serializer.Deserialize<FeatureSendReply>(response.Payload.Memory);
        var dispatched = Assert.Single(handler.Requests);
        Assert.Equal(RpcStatus.Ok, response.Status);
        Assert.Equal(ClusterSendStatus.Accepted, (ClusterSendStatus)reply.Status);
        Assert.Equal(new byte[] { 7 }, reply.Payload);
        Assert.Equal("matchmaking", dispatched.Feature.Value);
        Assert.Equal("join", dispatched.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, dispatched.Payload.ToArray());
        Assert.Equal(new NodeId("gateway-1"), dispatched.SourceNode);
        Assert.Equal("corr-1", dispatched.CorrelationId);
    }

    [Fact]
    public async Task BinderConvertsNullFeatureKindToBlankForTypedRejection()
    {
        var registry = new RpcServiceRegistry();
        var handler = new InvalidKindRejectingFeatureHandler();
        FeatureMessageBinder.Bind(registry, handler);
        Assert.True(registry.TryGetHandler(
            ClusterProtocol.ServiceId,
            ClusterProtocol.FeatureMessageMethodId,
            out var rpcHandler));

        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        using var payload = serializer.SerializeFrame(new FeatureSendRequest
        {
            Feature = "matchmaking",
            Kind = null!,
            Payload = Array.Empty<byte>(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            SourceNode = "gateway-1",
            CorrelationId = "corr-1"
        });
        using var frame = await rpcHandler!(
            session,
            new RpcRequestFrame(
                1,
                ClusterProtocol.ServiceId,
                ClusterProtocol.FeatureMessageMethodId,
                payload),
            TestContext.Current.CancellationToken);

        using var response = RpcEnvelopeCodec.DecodeResponse(frame);
        var reply = serializer.Deserialize<FeatureSendReply>(response.Payload.Memory);
        var dispatched = Assert.Single(handler.Requests);
        Assert.Equal(RpcStatus.Ok, response.Status);
        Assert.Equal(ClusterSendStatus.Rejected, (ClusterSendStatus)reply.Status);
        Assert.Equal("", dispatched.Kind);
    }

    private static ClusterNodeDescriptor NewTarget()
    {
        return new ClusterNodeDescriptor(
            new NodeId("data-1"),
            NodeState.Ready,
            new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
            {
                ["cluster"] = new NodeEndpoint("tcp://10.0.0.1:21001")
            },
            [new NodeFeatureDescriptor("matchmaking")]);
    }

    private static FeatureMessageRequest NewRequest()
    {
        return new FeatureMessageRequest(
            new FeatureName("matchmaking"),
            "join",
            new byte[] { 1, 2, 3 },
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("gateway-1"),
            "corr-1");
    }

    private sealed class RecordingClientFactory : IClusterClientFactory
    {
        private readonly IRpcClient _client;

        public RecordingClientFactory(IRpcClient client)
        {
            _client = client;
        }

        public List<RouteLocation> Targets { get; } = new();

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Targets.Add(target);
            return new ValueTask<IRpcClient>(_client);
        }
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        private readonly FeatureSendReply _reply;

        public RecordingRpcClient(FeatureSendReply reply)
        {
            _reply = reply;
        }

        public int ServiceId { get; private set; }

        public int MethodId { get; private set; }

        public FeatureSendRequest? Request { get; private set; }

        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ServiceId = method.ServiceId;
            MethodId = method.MethodId;
            Request = Assert.IsType<FeatureSendRequest>(arg);
            return new ValueTask<TResult>((TResult)(object)_reply);
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }

    private sealed class RecordingFeatureHandler : IFeatureMessageHandler
    {
        private readonly FeatureMessageReply _reply;

        public RecordingFeatureHandler(FeatureMessageReply reply)
        {
            _reply = reply;
        }

        public List<FeatureMessageRequest> Requests { get; } = new();

        public ValueTask<FeatureMessageReply> HandleAsync(
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return new ValueTask<FeatureMessageReply>(_reply);
        }
    }

    private sealed class InvalidKindRejectingFeatureHandler : IFeatureMessageHandler
    {
        public List<FeatureMessageRequest> Requests { get; } = new();

        public ValueTask<FeatureMessageReply> HandleAsync(
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var status = string.IsNullOrWhiteSpace(request.Kind)
                ? ClusterSendStatus.Rejected
                : ClusterSendStatus.Accepted;
            return new ValueTask<FeatureMessageReply>(
                new FeatureMessageReply(status, ReadOnlyMemory<byte>.Empty));
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
            return new ValueTask<TransportFrame>(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }
}
