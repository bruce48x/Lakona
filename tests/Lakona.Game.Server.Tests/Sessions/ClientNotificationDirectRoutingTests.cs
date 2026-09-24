using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Tests.Testing;
using Xunit;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class ClientNotificationDirectRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Process_backpressure_does_not_retain_rejected_session_queues(bool generated)
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Gateway();
        var busy = Session(gateway, "busy");
        var remote = new OrderedRemoteDispatcher();
        await using var router = Router(gateway, remote, capacityPerSession: 1, totalCapacity: 1);

        Assert.Equal(ClientNotificationStatus.Accepted, Enqueue(busy, "first"));
        await remote.FirstStarted.Task.WaitAsync(ct);
        try
        {
            for (var index = 0; index < 2_000; index++)
                Assert.Equal(ClientNotificationStatus.Backpressure, Enqueue(Session(gateway, $"rejected-{index}"), "rejected"));
            Parallel.For(0, 128, new ParallelOptions { CancellationToken = ct }, _ =>
                Assert.Equal(ClientNotificationStatus.Backpressure, Enqueue(Session(gateway, "rejected-0"), "rejected")));
            Assert.Equal(1, router.QueueCount);
            await router.WaitForIdleAsync(Session(gateway, "rejected-0"), ct);
        }
        finally
        {
            remote.ReleaseFirst.TrySetResult();
        }

        await router.WaitForIdleAsync(busy, ct);
        Assert.Equal(0, router.QueueCount);
        var recovered = Session(gateway, "rejected-0");
        Assert.Equal(ClientNotificationStatus.Accepted, Enqueue(recovered, "second"));
        await router.WaitForIdleAsync(recovered, ct);
        Assert.Equal(0, router.QueueCount);
        Assert.Equal(["first", "second"], remote.Delivered);

        ClientNotificationStatus Enqueue(GameSessionKey session, string payload) => generated
            ? router.EnqueueGenerated<ITestCallback, string>(session, 1, 1, "Notify", payload)
            : router.Enqueue(new ClientNotificationCommand
            {
                OwnerKey = session.OwnerKey,
                SessionId = session.SessionId,
                CallbackContractType = "test",
                MethodName = "Notify",
                Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload)
            });
    }

    [Fact]
    public async Task Concurrent_rejection_and_shutdown_leave_no_queues_or_deliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Gateway();
        var remote = new BlockingRemoteDispatcher();
        await using var router = Router(gateway, remote, capacityPerSession: 1, totalCapacity: 1);
        Assert.Equal(ClientNotificationStatus.Accepted,
            router.EnqueueGenerated<ITestCallback, string>(Session(gateway, "busy"), 1, 1, "Notify", "busy"));
        await remote.FirstStarted.Task.WaitAsync(ct);
        Assert.Equal(ClientNotificationStatus.Backpressure,
            router.EnqueueGenerated<ITestCallback, string>(Session(gateway, "before-stop"), 1, 1, "Notify", "rejected"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producers = Enumerable.Range(0, 8).Select(producer => Task.Run(async () =>
        {
            await start.Task.WaitAsync(ct);
            for (var index = 0; index < 256; index++)
            {
                var status = router.EnqueueGenerated<ITestCallback, string>(
                    Session(gateway, $"{producer}-{index}"), 1, 1, "Notify", "rejected");
                // Admission may win the race with shutdown; its drain must still
                // release the work without another delivery after cancellation.
                Assert.True(status is ClientNotificationStatus.Backpressure or ClientNotificationStatus.Failed or ClientNotificationStatus.Accepted);
            }
        }, ct)).ToArray();
        var stopping = Task.Run(async () =>
        {
            await start.Task.WaitAsync(ct);
            await router.DisposeAsync();
        }, ct);

        start.SetResult();
        await Task.WhenAll(producers.Append(stopping)).WaitAsync(ct);
        Assert.Equal(0, router.QueueCount);
        Assert.False(remote.BothStarted.Task.IsCompleted);
        Assert.Equal(ClientNotificationStatus.Failed,
            router.EnqueueGenerated<ITestCallback, string>(Session(gateway, "after-stop"), 1, 1, "Notify", "rejected"));
        Assert.Equal(0, router.QueueCount);
    }

    [Theory]
    [InlineData(ClientNotificationStatus.Accepted)]
    [InlineData(ClientNotificationStatus.Failed)]
    [InlineData(ClientNotificationStatus.Retained)]
    public async Task RpcResponseWaitsForRemotePublicationAndRejectsUndeliveredSuccess(ClientNotificationStatus finalStatus)
    {
        var gateway = Gateway();
        var session = Session(gateway, "player-1");
        var remote = new OrderedRemoteDispatcher { FinalStatus = finalStatus };
        await using var router = Router(gateway, remote, capacityPerSession: 4, totalCapacity: 8);
        var registry = new RpcServiceRegistry();
        registry.RegisterRaw(1, 1, (_, _, _, _) =>
        {
            Assert.Equal(ClientNotificationStatus.Accepted,
                router.EnqueueGenerated<ITestCallback, string>(session, 1, 1, "Notify", "first"));
            return new ValueTask<RpcRawResult>(RpcRawResult.Ok(ReadOnlyMemory<byte>.Empty));
        });
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var server = TestRpcSession.Create(serverTransport, new JsonRpcSerializer(), ownsTransport: false, registry: registry);
        await using var client = new RpcClientRuntime(clientTransport, new JsonRpcSerializer());
        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.StartAsync(TestContext.Current.CancellationToken);
        var call = client.CallAsync(new RpcMethod<int, RpcVoid>(1, 1), 0, TestContext.Current.CancellationToken).AsTask();
        try
        {
            await remote.FirstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(call.IsCompleted);
        }
        finally { remote.ReleaseFirst.TrySetResult(); }
        if (finalStatus == ClientNotificationStatus.Accepted)
            await call.WaitAsync(TestContext.Current.CancellationToken);
        else
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => call.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Equal(RpcStatus.InternalError, error.Status);
        }
        Assert.Equal(["first"], remote.Delivered);
    }

    [Fact]
    public async Task Accepted_notifications_are_fifo_per_session()
    {
        var gateway = Gateway();
        var session = Session(gateway, "player-1");
        var remote = new OrderedRemoteDispatcher();
        await using var router = Router(gateway, remote, capacityPerSession: 4, totalCapacity: 8);

        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<ITestCallback, string>(session, 1, 1, "Notify", "first"));
        await remote.FirstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<ITestCallback, string>(session, 1, 1, "Notify", "second"));
        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<ITestCallback, string>(session, 1, 1, "Notify", "third"));
        Assert.Equal(1, remote.StartedCount);

        remote.ReleaseFirst.TrySetResult();
        await router.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(["first", "second", "third"], remote.Delivered);
    }

    [Fact]
    public async Task Per_session_and_process_bounds_reject_without_blocking_other_sessions()
    {
        using var metrics = new MetricReasonCollector(
            LakonaGameServerTelemetry.SessionMeterName,
            "lakona.game.notification.backpressure",
            "lakona.game.notification.reason");
        var gateway = Gateway();
        var first = Session(gateway, "player-1");
        var second = Session(gateway, "player-2");
        var remote = new BlockingRemoteDispatcher();
        await using var router = Router(gateway, remote, capacityPerSession: 2, totalCapacity: 3);

        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<ITestCallback, string>(first, 1, 1, "Notify", "one"));
        await remote.FirstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<ITestCallback, string>(first, 1, 1, "Notify", "two"));
        Assert.Equal(ClientNotificationStatus.Backpressure, router.EnqueueGenerated<ITestCallback, string>(first, 1, 1, "Notify", "three"));
        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<ITestCallback, string>(second, 1, 1, "Notify", "other"));
        await remote.BothStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClientNotificationStatus.Backpressure, router.EnqueueGenerated<ITestCallback, string>(second, 1, 1, "Notify", "overflow"));

        Assert.Contains("session_capacity", metrics.Reasons);
        Assert.Contains("process_capacity", metrics.Reasons);

        remote.Release.TrySetResult();
        await router.WaitForIdleAsync(first, TestContext.Current.CancellationToken);
        await router.WaitForIdleAsync(second, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exact_ready_gateway_accepts_without_a_route_directory()
    {
        var gateway = Gateway();
        var runtime = new RecordingRuntime();
        var dispatcher = new ClientNotificationOwnerDispatcher(runtime, new TestClusterMembership(Snapshot(gateway)), gateway.Node);

        var status = await dispatcher.DispatchAsync(Command(gateway), TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
        Assert.Single(runtime.Published);
    }

    [Fact]
    public async Task Replaced_gateway_incarnation_is_rejected_before_outbox_mutation()
    {
        var oldGateway = Gateway();
        var replacement = new NodeReference(oldGateway.Cluster, oldGateway.Node, NodeIncarnationId.New());
        var runtime = new RecordingRuntime();
        var dispatcher = new ClientNotificationOwnerDispatcher(runtime, new TestClusterMembership(Snapshot(replacement)), replacement.Node);

        var status = await dispatcher.DispatchAsync(Command(oldGateway), TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.StateLost, status);
        Assert.Empty(runtime.Published);
    }

    [Fact]
    public async Task Malformed_locator_is_rejected_without_outbox_mutation()
    {
        var gateway = Gateway();
        var runtime = new RecordingRuntime();
        var dispatcher = new ClientNotificationOwnerDispatcher(runtime, new TestClusterMembership(Snapshot(gateway)), gateway.Node);
        var command = Command(gateway);
        command.SessionId = "not-a-session-locator";

        var status = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
        Assert.Empty(runtime.Published);
    }

    [Fact]
    public async Task Same_gateway_resume_keeps_the_exact_locator_authority()
    {
        var gateway = Gateway();
        var runtime = new RecordingRuntime();
        var dispatcher = new ClientNotificationOwnerDispatcher(runtime, new TestClusterMembership(Snapshot(gateway)), gateway.Node);
        var command = Command(gateway);

        Assert.Equal(ClientNotificationStatus.Accepted,
            await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken));
        await runtime.ReplayPendingAsync(new GameSessionKey(command.OwnerKey, command.SessionId), TestContext.Current.CancellationToken);

        Assert.Single(runtime.Published);
        Assert.Single(runtime.Resumed);
    }

    [Fact]
    public async Task Closed_authority_gate_rejects_before_outbox_mutation()
    {
        var gateway = Gateway();
        var runtime = new RecordingRuntime();
        var dispatcher = new ClientNotificationOwnerDispatcher(runtime, new TestClusterMembership(Snapshot(gateway)), gateway.Node, new ClosedGate());

        var status = await dispatcher.DispatchAsync(Command(gateway), TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Failed, status);
        Assert.Empty(runtime.Published);
    }

    private static ClientNotificationCommand Command(NodeReference gateway) => new()
    {
        OwnerKey = "player/1",
        SessionId = MembershipSessionLocator.Encode(gateway),
        CallbackContractType = "test",
        MethodName = "Notify"
    };

    private static NodeReference Gateway() => new(
        new ClusterIncarnationId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new NodeId("gateway-a"),
        new NodeIncarnationId(Guid.Parse("22222222-2222-2222-2222-222222222222")));

    private static GameSessionKey Session(NodeReference gateway, string owner) =>
        new(owner, MembershipSessionLocator.Encode(gateway));

    private static ClientNotificationCommandRouter Router(
        NodeReference gateway,
        IClientNotificationRemoteDispatcher remote,
        int capacityPerSession,
        int totalCapacity) => new(
            new RecordingRuntime(),
            new TestClusterMembership(Snapshot(gateway)),
            remote,
            new NodeId("producer"),
            capacityPerSession: capacityPerSession,
            totalCapacity: totalCapacity);

    private static ClusterMembershipSnapshot Snapshot(NodeReference gateway) => new(
        gateway.Cluster,
        new MembershipViewId(4),
        [new ClusterMember(gateway, ClusterMemberState.Active, new NodeEndpoint("tcp://gateway:2000"))]);

    private sealed class ClosedGate : IDistributedWorkAdmissionGate
    {
        public bool IsOpen => false;
        public bool TryEnter(out DistributedWorkAdmission admission) { admission = default; return false; }
        public void Exit(DistributedWorkAdmission admission) => throw new InvalidOperationException();
    }

    private sealed class RecordingRuntime : IReliablePushRuntime
    {
        public List<GameSessionKey> Published { get; } = [];
        public List<GameSessionKey> Resumed { get; } = [];
        public ValueTask<ClientNotificationStatus> PublishAsync(GameSessionKey session, ClientNotificationCommand command, CancellationToken cancellationToken = default)
        {
            Published.Add(session);
            return new(ClientNotificationStatus.Accepted);
        }

        public ValueTask ReplayPendingAsync(GameSessionKey session, CancellationToken cancellationToken = default)
        {
            Resumed.Add(session);
            return default;
        }
        public ValueTask<ReliablePushAckOutcome> AckAsync(GameSessionKey currentSession, GameSessionKey acknowledgedSession, long sequence, CancellationToken cancellationToken = default) => new(ReliablePushAckOutcome.Accepted());
    }

    private interface ITestCallback;

    private sealed class OrderedRemoteDispatcher : IClientNotificationRemoteDispatcher
    {
        private int started;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartedCount => Volatile.Read(ref started);
        public List<string> Delivered { get; } = [];
        public ClientNotificationStatus FinalStatus { get; init; } = ClientNotificationStatus.Accepted;

        public async ValueTask<ClientNotificationStatus> DispatchAsync(RouteLocation target, ClientNotificationCommand command, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
            Delivered.Add(System.Text.Json.JsonSerializer.Deserialize<string>(command.Payload)!);
            return FinalStatus;
        }
    }

    private sealed class BlockingRemoteDispatcher : IClientNotificationRemoteDispatcher
    {
        private int started;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ClientNotificationStatus> DispatchAsync(RouteLocation target, ClientNotificationCommand command, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref started);
            if (count >= 1) FirstStarted.TrySetResult();
            if (count >= 2) BothStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return ClientNotificationStatus.Accepted;
        }
    }
}
