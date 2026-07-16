using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationAdmissionTests
{
    [Fact]
    public async Task Enqueue_returns_after_framework_admission_before_remote_delivery_completes()
    {
        var session = new GameSessionKey("player-1", "session-a", 1);
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            TestContext.Current.CancellationToken);
        var remote = new BlockingRemoteDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            new LocalClientNotificationCommandDispatcher(new InMemoryGameSessionRegistry()),
            routes,
            remote,
            new NodeId("battle-1"));
        var command = ClientNotificationCommandFactory.Create<ITestCallback>(
            session,
            callback => callback.Notify("tick"))!;

        var status = router.Enqueue(command);

        try
        {
            Assert.Equal(ClientNotificationStatus.Accepted, status);
            await remote.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(remote.DeliveryCompleted);
        }
        finally
        {
            remote.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Accepted_notifications_are_delivered_in_fifo_order_per_session()
    {
        var session = new GameSessionKey("player-1", "session-a", 1);
        var routes = await CreateRemoteRouteAsync(session);
        var remote = new OrderedRemoteDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            new LocalClientNotificationCommandDispatcher(new InMemoryGameSessionRegistry()),
            routes,
            remote,
            new NodeId("battle-1"));

        var first = router.EnqueueGenerated<ITestCallback, string>(
            session, 1, 1, nameof(ITestCallback.Notify), "first");
        await remote.FirstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = router.EnqueueGenerated<ITestCallback, string>(
            session, 1, 1, nameof(ITestCallback.Notify), "second");
        var third = router.EnqueueGenerated<ITestCallback, string>(
            session, 1, 1, nameof(ITestCallback.Notify), "third");

        Assert.Equal(ClientNotificationStatus.Accepted, first);
        Assert.Equal(ClientNotificationStatus.Accepted, second);
        Assert.Equal(ClientNotificationStatus.Accepted, third);
        Assert.Equal(1, remote.StartedCount);

        remote.ReleaseFirst.TrySetResult();
        await router.WaitForIdleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second", "third"], remote.Delivered);
    }

    [Fact]
    public async Task Admission_returns_backpressure_when_the_session_queue_is_full()
    {
        var session = new GameSessionKey("player-1", "session-a", 1);
        var routes = await CreateRemoteRouteAsync(session);
        var remote = new BlockingRemoteDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            new LocalClientNotificationCommandDispatcher(new InMemoryGameSessionRegistry()),
            routes,
            remote,
            new NodeId("battle-1"),
            capacityPerSession: 2);

        var first = router.EnqueueGenerated<ITestCallback, string>(
            session, 1, 1, nameof(ITestCallback.Notify), "first");
        await remote.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = router.EnqueueGenerated<ITestCallback, string>(
            session, 1, 1, nameof(ITestCallback.Notify), "second");
        var rejected = router.EnqueueGenerated<ITestCallback, string>(
            session, 1, 1, nameof(ITestCallback.Notify), "third");

        Assert.Equal(ClientNotificationStatus.Accepted, first);
        Assert.Equal(ClientNotificationStatus.Accepted, second);
        Assert.Equal(ClientNotificationStatus.Backpressure, rejected);

        remote.Release.TrySetResult();
        await router.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Slow_delivery_for_one_session_does_not_stall_another_session()
    {
        var firstSession = new GameSessionKey("player-1", "session-a", 1);
        var secondSession = new GameSessionKey("player-2", "session-b", 1);
        var routes = await CreateRemoteRouteAsync(firstSession);
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(secondSession),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: secondSession.Generation),
            TestContext.Current.CancellationToken);
        var remote = new ConcurrentBlockingRemoteDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            new LocalClientNotificationCommandDispatcher(new InMemoryGameSessionRegistry()),
            routes,
            remote,
            new NodeId("battle-1"));

        var first = router.EnqueueGenerated<ITestCallback, string>(
            firstSession, 1, 1, nameof(ITestCallback.Notify), "first");
        var second = router.EnqueueGenerated<ITestCallback, string>(
            secondSession, 1, 1, nameof(ITestCallback.Notify), "second");

        Assert.Equal(ClientNotificationStatus.Accepted, first);
        Assert.Equal(ClientNotificationStatus.Accepted, second);
        await remote.BothStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        remote.Release.TrySetResult();
        await router.WaitForIdleAsync(firstSession, TestContext.Current.CancellationToken);
        await router.WaitForIdleAsync(secondSession, TestContext.Current.CancellationToken);
    }

    private static async Task<InMemoryRouteDirectory> CreateRemoteRouteAsync(GameSessionKey session)
    {
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            TestContext.Current.CancellationToken);
        return routes;
    }

    private interface ITestCallback
    {
        void Notify(string message);
    }

    private sealed class BlockingRemoteDispatcher : IClientNotificationRemoteDispatcher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DeliveryCompleted { get; private set; }

        public async ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            DeliveryCompleted = true;
            return ClientNotificationStatus.Accepted;
        }
    }

    private sealed class OrderedRemoteDispatcher : IClientNotificationRemoteDispatcher
    {
        private int _startedCount;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartedCount => Volatile.Read(ref _startedCount);

        public List<string> Delivered { get; } = [];

        public async ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _startedCount) == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            Delivered.Add(System.Text.Json.JsonSerializer.Deserialize<string>(command.Payload)!);
            return ClientNotificationStatus.Accepted;
        }
    }

    private sealed class ConcurrentBlockingRemoteDispatcher : IClientNotificationRemoteDispatcher
    {
        private int _startedCount;

        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _startedCount) == 2)
            {
                BothStarted.TrySetResult();
            }

            await Release.Task.WaitAsync(cancellationToken);
            return ClientNotificationStatus.Accepted;
        }
    }
}
