using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class RemoteActorInvokerTests
{
    [Fact]
    public async Task TellAsync_maps_cluster_backpressure_to_remote_backpressure()
    {
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender { Status = ClusterSendStatus.Backpressure };
        var invoker = CreateInvoker(nodeSender: sender);

        var result = await invoker.TellAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Backpressure, result.Status);
    }

    [Fact]
    public async Task TellAsync_maps_stale_route_to_node_unavailable()
    {
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender { Status = ClusterSendStatus.StaleRoute };
        var invoker = CreateInvoker(nodeSender: sender);

        var result = await invoker.TellAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.NodeUnavailable, result.Status);
    }

    [Fact]
    public async Task TellAsync_sends_envelope_without_reply_correlation()
    {
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender();
        var invoker = CreateInvoker(nodeSender: sender);

        var result = await invoker.TellAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Accepted, result.Status);
        Assert.NotNull(sender.LastMessage);
        Assert.True(ClusterActorEnvelope.TryFromClusterMessage(sender.LastMessage, out var envelope));
        Assert.NotNull(envelope);
        Assert.Equal(invocation.ActorId.Value, envelope.ActorId);
        Assert.Equal(invocation.MethodName, envelope.Kind);
        Assert.Equal(invocation.Payload.ToArray(), envelope.Payload.ToArray());
        Assert.Equal(new NodeId("node-local"), envelope.SourceNode);
        Assert.Equal(invocation.CorrelationId, envelope.CorrelationId);
        Assert.Null(envelope.ReplyCorrelationId);
    }

    [Fact]
    public async Task AskAsync_sends_envelope_with_reply_correlation_and_returns_reply()
    {
        var gateway = new RemoteActorGateway();
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender();
        var invoker = CreateInvoker(gateway, sender);
        var replyPayload = new byte[] { 9, 8, 7 };
        sender.OnSend = message =>
        {
            Assert.True(ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope));
            Assert.NotNull(envelope);
            Assert.Equal(invocation.CorrelationId, envelope.ReplyCorrelationId);
            _ = gateway.CreateReplyHandler().HandleAsync(
                new ClusterMessage(
                    ClusterActorRouteKeys.ForReply(new NodeId("node-local")),
                    RemoteActorGateway.ReplyKind,
                    replyPayload,
                    DateTimeOffset.UtcNow.AddSeconds(5),
                    invocation.Node,
                    invocation.CorrelationId),
                TestContext.Current.CancellationToken);
        };

        var result = await invoker.AskAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Replied, result.Status);
        Assert.Equal(replyPayload, result.Payload.ToArray());
    }

    [Fact]
    public async Task AskAsync_send_failure_releases_pending_reply_immediately()
    {
        var gateway = new RemoteActorGateway();
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender { Status = ClusterSendStatus.Backpressure };
        var invoker = CreateInvoker(gateway, sender);

        var result = await invoker.AskAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Backpressure, result.Status);

        var pending = gateway.RegisterPendingAsync(
            invocation.CorrelationId,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var replyPayload = new byte[] { 4, 5, 6 };

        await gateway.CreateReplyHandler().HandleAsync(
            new ClusterMessage(
                ClusterActorRouteKeys.ForReply(new NodeId("node-local")),
                RemoteActorGateway.ReplyKind,
                replyPayload,
                DateTimeOffset.UtcNow.AddSeconds(5),
                invocation.Node,
                invocation.CorrelationId),
            TestContext.Current.CancellationToken);

        var payload = await pending.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(replyPayload, payload.ToArray());
    }

    [Fact]
    public async Task AskAsync_send_exception_releases_pending_reply_and_propagates()
    {
        var gateway = new RemoteActorGateway();
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender
        {
            ExceptionToThrow = new InvalidOperationException("send failed")
        };
        var invoker = CreateInvoker(gateway, sender);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await invoker.AskAsync(invocation, TestContext.Current.CancellationToken));

        Assert.Equal("send failed", exception.Message);

        var pending = gateway.RegisterPendingAsync(
            invocation.CorrelationId,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var replyPayload = new byte[] { 7, 8, 9 };

        await gateway.CreateReplyHandler().HandleAsync(
            new ClusterMessage(
                ClusterActorRouteKeys.ForReply(new NodeId("node-local")),
                RemoteActorGateway.ReplyKind,
                replyPayload,
                DateTimeOffset.UtcNow.AddSeconds(5),
                invocation.Node,
                invocation.CorrelationId),
            TestContext.Current.CancellationToken);

        var payload = await pending.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(replyPayload, payload.ToArray());
    }

    [Fact]
    public async Task AskAsync_send_cancellation_releases_pending_reply_and_returns_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new RemoteActorGateway();
        var invocation = CreateInvocation();
        var sender = new RecordingClusterNodeSender
        {
            OnSend = _ => cancellation.Cancel(),
            ExceptionToThrow = new OperationCanceledException(cancellation.Token)
        };
        var invoker = CreateInvoker(gateway, sender);

        var result = await invoker.AskAsync(invocation, cancellation.Token);

        Assert.Equal(RemoteActorStatus.Cancelled, result.Status);

        var pending = gateway.RegisterPendingAsync(
            invocation.CorrelationId,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var replyPayload = new byte[] { 1, 3, 5 };

        await gateway.CreateReplyHandler().HandleAsync(
            new ClusterMessage(
                ClusterActorRouteKeys.ForReply(new NodeId("node-local")),
                RemoteActorGateway.ReplyKind,
                replyPayload,
                DateTimeOffset.UtcNow.AddSeconds(5),
                invocation.Node,
                invocation.CorrelationId),
            TestContext.Current.CancellationToken);

        var payload = await pending.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(replyPayload, payload.ToArray());
    }

    [Fact]
    public async Task TellAsync_sends_to_invocation_node_through_cluster_node_sender()
    {
        var requestedNode = new NodeId("node-requested");
        var sender = new RecordingClusterNodeSender();
        var invoker = CreateInvoker(nodeSender: sender);
        var invocation = CreateInvocation(node: requestedNode);

        var result = await invoker.TellAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Accepted, result.Status);
        Assert.Equal(requestedNode, sender.LastNode);
        Assert.Equal(ClusterActorRouteKeys.ForActor(invocation.ActorId.Value), sender.LastRoute);
        Assert.NotNull(sender.LastMessage);
        Assert.True(ClusterActorEnvelope.TryFromClusterMessage(sender.LastMessage, out var envelope));
        Assert.NotNull(envelope);
        Assert.Equal(invocation.ActorId.Value, envelope.ActorId);
    }

    [Fact]
    public async Task AskAsync_returns_expired_without_sending_when_deadline_has_passed()
    {
        var invocation = CreateInvocation(deadline: DateTimeOffset.UtcNow.AddSeconds(-1));
        var sender = new RecordingClusterNodeSender();
        var invoker = CreateInvoker(nodeSender: sender);

        var result = await invoker.AskAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Expired, result.Status);
        Assert.Null(sender.LastMessage);
    }

    [Fact]
    public void RemoteActorOptions_only_exposes_actor_call_options()
    {
        Assert.NotNull(typeof(RemoteActorOptions).GetProperty(nameof(RemoteActorOptions.DefaultTimeout)));
        Assert.Null(typeof(RemoteActorOptions).GetProperty("ClusterName"));
        Assert.Null(typeof(RemoteActorOptions).GetProperty("EndpointName"));
    }

    [Fact]
    public void RemoteActorInvocation_copies_payload()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var invocation = new RemoteActorInvocation(
            new NodeId("node-a"),
            ActorId.From("room/1001"),
            "room",
            "join",
            bytes,
            DateTimeOffset.UtcNow.AddSeconds(10),
            "corr-1");

        bytes[0] = 9;

        Assert.Equal(1, invocation.Payload.ToArray()[0]);
    }

    [Fact]
    public void RemoteActorInvocationResult_copies_payload()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = RemoteActorInvocationResult.Replied(bytes);

        bytes[0] = 9;

        Assert.Equal(1, result.Payload.ToArray()[0]);
    }

    [Theory]
    [InlineData(typeof(RemoteActorInvocation))]
    [InlineData(typeof(RemoteActorInvocationResult))]
    public void RemoteActor_payload_has_no_public_setter(Type type)
    {
        var payload = type.GetProperty(nameof(RemoteActorInvocation.Payload));

        Assert.NotNull(payload);
        Assert.Null(payload.SetMethod);
    }

    [Fact]
    public void RemoteActorException_preserves_structured_failure_fields()
    {
        var exception = new RemoteActorException(
            RemoteActorStatus.RouteNotFound,
            ActorId.From("room/1001"),
            "room",
            "join",
            "The route was not found.",
            new NodeId("node-a"),
            "corr-1");

        Assert.Equal(RemoteActorStatus.RouteNotFound, exception.Status);
        Assert.Equal(ActorId.From("room/1001"), exception.ActorId);
        Assert.Equal("room", exception.ActorName);
        Assert.Equal("join", exception.MethodName);
        Assert.Equal(new NodeId("node-a"), exception.Node);
        Assert.Equal("corr-1", exception.CorrelationId);
        Assert.Contains("RouteNotFound", exception.Message);
    }

    private static RemoteActorInvocation CreateInvocation(
        DateTimeOffset? deadline = null,
        NodeId? node = null)
    {
        return new RemoteActorInvocation(
            node ?? new NodeId("node-b"),
            ActorId.From("room/1001"),
            "room",
            "leave",
            new byte[] { 1, 2, 3 },
            deadline ?? DateTimeOffset.UtcNow.AddSeconds(5),
            "corr-1");
    }

    private static RemoteActorInvoker CreateInvoker(
        RemoteActorGateway? gateway = null,
        RecordingClusterNodeSender? nodeSender = null)
    {
        return new RemoteActorInvoker(
            gateway ?? new RemoteActorGateway(),
            new NodeId("node-local"),
            nodeSender ?? new RecordingClusterNodeSender(),
            new RemoteActorOptions());
    }

    private sealed class RecordingClusterNodeSender : IClusterNodeSender
    {
        public NodeId LastNode { get; private set; }

        public ClusterMessage? LastMessage { get; private set; }

        public RouteKey LastRoute { get; private set; } = default!;

        public ClusterSendStatus Status { get; set; } = ClusterSendStatus.Accepted;

        public Action<ClusterMessage>? OnSend { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            LastNode = nodeId;
            LastRoute = route;
            LastMessage = message;
            OnSend?.Invoke(message);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ValueTask.FromResult(Status);
        }
    }
}
