using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

public sealed class RemoteActorGatewayTests
{
    private static readonly byte[] EchoPayload = [0x0a, 0x0b, 0x0c];
    private const string EchoKind = "echo";

    [Fact]
    public async Task AskRemoteAsync_sends_request_and_receives_node_directed_reply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var nodeA = new NodeId("node-a");
        var nodeB = new NodeId("node-b");

        var routeDirectory = new InMemoryRouteDirectory();
        var nodeDirectory = new InMemoryNodeDirectory();
        var messenger = new InMemoryLoopbackNodeMessenger();
        await RegisterNodeAsync(nodeDirectory, nodeA, now, cancellationToken);
        await RegisterNodeAsync(nodeDirectory, nodeB, now, cancellationToken);
        var nodeSenderA = new ClusterNodeSender(nodeDirectory, messenger);
        var nodeSenderB = new ClusterNodeSender(nodeDirectory, messenger);

        var providerB = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var hostingB = providerB.GetRequiredService<ActorHosting>();
        var runtimeB = providerB.GetRequiredService<IActorRuntime>();
        await hostingB.CreateAsync<DummyActor>(ActorId.From("echo/1"), cancellationToken);

        var handlerB = new ClusterActorDispatcher<DummyActor>(
            runtimeB,
            async (actor, envelope, ct) =>
            {
                var status = await RemoteActorGateway.SendReplyAsync(
                    nodeSenderB,
                    replyingNode: nodeB,
                    destinationNode: envelope.SourceNode,
                    envelope.ReplyCorrelationId!,
                    envelope.Payload,
                    ct);
                return status;
            });

        messenger.RegisterNode(nodeB, handlerB);

        var actorRoute = new RouteLocation(
            ClusterActorRouteKeys.ForActor("echo/1"),
            nodeB,
            new NodeEndpoint("in-memory://node-b"),
            now.AddMinutes(10));
        await routeDirectory.RegisterAsync(actorRoute, cancellationToken);

        var providerA = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtimeA = providerA.GetRequiredService<IActorRuntime>();
        var gatewayA = new RemoteActorGateway();
        var routerA = new ClusterRouter(nodeA, routeDirectory, new RecordingHandler(), messenger, () => now);

        var replyHandler = new RecordingReplyHandler(gatewayA.CreateReplyHandler());
        messenger.RegisterNode(nodeA, replyHandler);

        var result = await runtimeA.AskRemoteAsync(
            routerA,
            gatewayA,
            new NodeId("node-a"),
            "echo/1",
            EchoKind,
            () => EchoPayload,
            static reply => reply,
            TimeSpan.FromSeconds(5),
            cancellationToken);

        Assert.Equal(EchoPayload, result.ToArray());
        Assert.NotNull(replyHandler.Message);
        Assert.Equal(RemoteActorGateway.ReplyKind, replyHandler.Message.Kind);
        Assert.Equal(nodeB, replyHandler.Message.SourceNode);
        Assert.Null(await routeDirectory.ResolveAsync(
            ClusterActorRouteKeys.ForReply(nodeA),
            now,
            cancellationToken));
    }

    [Fact]
    public async Task AskRemoteAsync_failed_request_send_removes_pending_registration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var routeDirectory = new InMemoryRouteDirectory();
        var messenger = new InMemoryLoopbackNodeMessenger();
        var gateway = new RemoteActorGateway();
        var router = new ClusterRouter(
            "node-a",
            routeDirectory,
            new RecordingHandler(),
            messenger);
        var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.AskRemoteAsync(
                router,
                gateway,
                new NodeId("node-a"),
                "missing/1",
                EchoKind,
                () => EchoPayload,
                static reply => reply,
                TimeSpan.FromSeconds(1),
                cancellationToken));

        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task Reply_handler_accepts_late_reply_after_timeout_without_recreating_pending_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gateway = new RemoteActorGateway();
        var pending = gateway.RegisterPendingAsync(
            "late-reply",
            TimeSpan.FromMilliseconds(20),
            cancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(async () => await pending);

        var status = await gateway.CreateReplyHandler().HandleAsync(
            new ClusterMessage(
                ClusterActorRouteKeys.ForReply("node-a"),
                RemoteActorGateway.ReplyKind,
                EchoPayload,
                DateTimeOffset.UtcNow.AddSeconds(30),
                "node-b",
                "late-reply"),
            cancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.False(gateway.TryCancelPending("late-reply"));
    }

    [Fact]
    public async Task TellRemoteAsync_sends_message_without_expecting_reply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        var directory = new InMemoryRouteDirectory();
        var messenger = new InMemoryLoopbackNodeMessenger();

        var providerB = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var hostingB = providerB.GetRequiredService<ActorHosting>();
        var runtimeB = providerB.GetRequiredService<IActorRuntime>();
        await hostingB.CreateAsync<DummyActor>(ActorId.From("target/1"), cancellationToken);
        var received = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handlerB = new ClusterActorDispatcher<DummyActor>(
            runtimeB,
            (actor, envelope, ct) =>
            {
                received.TrySetResult(envelope.Payload);
                return ValueTask.FromResult(ClusterSendStatus.Accepted);
            });

        messenger.RegisterNode("node-b", handlerB);

        var actorRoute = new RouteLocation(
            ClusterActorRouteKeys.ForActor("target/1"),
            "node-b",
            new NodeEndpoint("in-memory://node-b"),
            now.AddMinutes(10));
        await directory.RegisterAsync(actorRoute, cancellationToken);

        messenger.RegisterNode("node-a", new RecordingHandler());
        var routerA = new ClusterRouter("node-a", directory, new RecordingHandler(), messenger, () => now);

        var providerA = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtimeA = providerA.GetRequiredService<IActorRuntime>();

        await ActorRuntimeRemoteExtensions.TellRemoteAsync(
            runtimeA,
            routerA,
            "node-a",
            "target/1",
            "notify",
            () => EchoPayload,
            TimeSpan.FromSeconds(5),
            cancellationToken);

        var receivedPayload = await received.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.Equal(EchoPayload, receivedPayload.ToArray());
    }

    [Fact]
    public async Task RegisterPendingAsync_times_out_when_reply_never_arrives()
    {
        var gateway = new RemoteActorGateway();

        var pending = gateway.RegisterPendingAsync(
            "missing-reply",
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.Contains("No reply received", exception.Message);
    }

    [Fact]
    public async Task Composite_handler_tries_handlers_in_order()
    {
        var handlerA = new StatusHandler(ClusterSendStatus.RouteNotFound);
        var handlerB = new StatusHandler(ClusterSendStatus.Accepted);
        var handlerC = new StatusHandler(ClusterSendStatus.Accepted);
        var composite = new CompositeClusterMessageHandler(handlerA, handlerB, handlerC);

        var status = await composite.HandleAsync(
            new ClusterMessage(
                "test/1",
                "cmd",
                Array.Empty<byte>(),
                DateTimeOffset.UtcNow.AddMinutes(1),
                "source"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.True(handlerA.Called);
        Assert.True(handlerB.Called);
        Assert.False(handlerC.Called);
    }

    private sealed class DummyActor : GameActor
    {
    }

    private sealed class RecordingHandler : IClusterMessageHandler
    {
        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ClusterSendStatus.Accepted);
        }
    }

    private sealed class RecordingReplyHandler(IClusterMessageHandler inner) : IClusterMessageHandler
    {
        public ClusterMessage? Message { get; private set; }

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            Message = message;
            return inner.HandleAsync(message, cancellationToken);
        }
    }

    private static async ValueTask RegisterNodeAsync(
        InMemoryNodeDirectory directory,
        NodeId node,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await directory.RegisterAsync(
            new NodeRegistration(
                "local",
                node,
                new Dictionary<string, NodeEndpoint>
                {
                    ["cluster"] = new NodeEndpoint($"in-memory://{node}")
                },
                now.AddMinutes(10),
                NodeState.Ready),
            now,
            cancellationToken);
    }

    private sealed class StatusHandler(ClusterSendStatus status) : IClusterMessageHandler
    {
        public bool Called { get; private set; }

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            return ValueTask.FromResult(status);
        }
    }
}
