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
    public async Task AskRemoteAsync_sends_request_and_receives_reply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        // Shared infrastructure
        var directory = new InMemoryRouteDirectory();
        var messenger = new InMemoryLoopbackNodeMessenger();

        // --- Node B: the target node ---
        var providerB = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtimeB = providerB.GetRequiredService<IActorRuntime>();
        var gatewayB = new RemoteActorGateway();
        var routerB = new ClusterRouter("node-b", directory, new RecordingHandler(), messenger, () => now);

        var handlerB = new ClusterActorDispatcher<DummyActor>(
            runtimeB,
            async (actor, envelope, ct) =>
            {
                if (envelope.ReplyCorrelationId is not null)
                {
                    await RemoteActorGateway.SendReplyAsync(
                        routerB,
                        envelope.SourceNode,
                        envelope.ReplyCorrelationId,
                        envelope.Payload,
                        ct);
                }

                return ClusterSendStatus.Accepted;
            });

        messenger.RegisterNode("node-b", handlerB);

        // Register Node B's actor route
        var actorRoute = new RouteLocation(
            ClusterActorRouteKeys.ForActor("echo/1"),
            "node-b",
            new NodeEndpoint("in-memory://node-b"),
            now.AddMinutes(10));
        await directory.RegisterAsync(actorRoute, cancellationToken);

        // --- Node A: the requesting node ---
        var providerA = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtimeA = providerA.GetRequiredService<IActorRuntime>();
        var gatewayA = new RemoteActorGateway();
        var routerA = new ClusterRouter("node-a", directory, new RecordingHandler(), messenger, () => now);

        // Node A's reply handler handles incoming replies
        var replyHandler = gatewayA.CreateReplyHandler();
        messenger.RegisterNode("node-a", new CompositeClusterMessageHandler(replyHandler, new RecordingHandler()));

        // Register reply route so Node B can send replies to Node A
        var replyRoute = new RouteLocation(
            ClusterActorRouteKeys.ForReply("node-a"),
            "node-a",
            new NodeEndpoint("in-memory://node-a"),
            now.AddMinutes(10));
        await directory.RegisterAsync(replyRoute, cancellationToken);

        // --- Send the remote request ---
        var result = await ActorRuntimeRemoteExtensions.AskRemoteAsync<ReadOnlyMemory<byte>>(
            runtimeA,
            routerA,
            gatewayA,
            directory,
            "node-a",
            "echo/1",
            EchoKind,
            () => EchoPayload,
            reply => reply,
            TimeSpan.FromSeconds(5),
            cancellationToken);

        Assert.Equal(EchoPayload, result.ToArray());
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
        var runtimeB = providerB.GetRequiredService<IActorRuntime>();
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
