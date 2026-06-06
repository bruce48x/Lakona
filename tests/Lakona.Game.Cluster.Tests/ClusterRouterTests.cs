using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterRouterTests
{
    [Fact]
    public async Task ExpiredMessageIsRejectedBeforeRouteLookup()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new CountingRouteDirectory();
        var router = new ClusterRouter(
            "local",
            directory,
            new RecordingHandler(ClusterSendStatus.Accepted),
            new RecordingMessenger(ClusterSendStatus.Accepted),
            () => now);

        var status = await router.SendAsync(
            NewMessage(expiresAt: now.AddSeconds(-1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Expired, status);
        Assert.Equal(0, directory.ResolveCount);
    }

    [Fact]
    public async Task MissingRouteReturnsRouteNotFound()
    {
        var now = DateTimeOffset.UtcNow;
        var router = new ClusterRouter(
            "local",
            new InMemoryRouteDirectory(),
            new RecordingHandler(ClusterSendStatus.Accepted),
            new RecordingMessenger(ClusterSendStatus.Accepted),
            () => now);

        var status = await router.SendAsync(
            NewMessage(expiresAt: now.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task LocalRouteDispatchesToLocalHandlerWithoutMessenger()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryRouteDirectory();
        var handler = new RecordingHandler(ClusterSendStatus.Accepted);
        var messenger = new RecordingMessenger(ClusterSendStatus.Accepted);
        await directory.RegisterAsync(
            new RouteLocation("room/1", "local", new NodeEndpoint("in-memory://local"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter("local", directory, handler, messenger, () => now);
        var message = NewMessage(expiresAt: now.AddMinutes(1));

        var status = await router.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Same(message, handler.Messages.Single());
        Assert.Empty(messenger.Messages);
    }

    [Fact]
    public async Task RemoteRouteUsesMessengerAndPreservesTraceMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryRouteDirectory();
        var handler = new RecordingHandler(ClusterSendStatus.Accepted);
        var messenger = new RecordingMessenger(ClusterSendStatus.Accepted);
        await directory.RegisterAsync(
            new RouteLocation("room/1", "remote", new NodeEndpoint("in-memory://remote"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter("local", directory, handler, messenger, () => now);
        var message = NewMessage(
            expiresAt: now.AddMinutes(1),
            correlationId: "corr-1",
            traceId: "trace-1",
            orderedBy: "room/1");

        var status = await router.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Empty(handler.Messages);
        var sent = Assert.Single(messenger.Messages);
        Assert.Equal(new NodeId("remote"), sent.Target.Node);
        Assert.Equal("in-memory://remote", sent.Target.Endpoint.Address);
        Assert.Same(message, sent.Message);
        Assert.Equal("corr-1", sent.Message.CorrelationId);
        Assert.Equal("trace-1", sent.Message.TraceId);
        Assert.Equal("room/1", sent.Message.OrderedBy);
    }

    [Fact]
    public async Task BackpressureStatusIsPropagated()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryRouteDirectory();
        await directory.RegisterAsync(
            new RouteLocation("room/1", "remote", new NodeEndpoint("in-memory://remote"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter(
            "local",
            directory,
            new RecordingHandler(ClusterSendStatus.Accepted),
            new RecordingMessenger(ClusterSendStatus.Backpressure),
            () => now);

        var status = await router.SendAsync(
            NewMessage(expiresAt: now.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Backpressure, status);
    }

    [Fact]
    public async Task TimeoutStatusIsPropagated()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryRouteDirectory();
        await directory.RegisterAsync(
            new RouteLocation("room/1", "remote", new NodeEndpoint("in-memory://remote"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter(
            "local",
            directory,
            new RecordingHandler(ClusterSendStatus.Accepted),
            new RecordingMessenger(ClusterSendStatus.Timeout),
            () => now);

        var status = await router.SendAsync(
            NewMessage(expiresAt: now.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Timeout, status);
    }

    [Fact]
    public async Task RemoteRouteCanUseLoopbackMessenger()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryRouteDirectory();
        var remoteHandler = new RecordingHandler(ClusterSendStatus.Accepted);
        var messenger = new InMemoryLoopbackNodeMessenger();
        messenger.RegisterNode("remote", remoteHandler);
        await directory.RegisterAsync(
            new RouteLocation("room/1", "remote", new NodeEndpoint("in-memory://remote"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter(
            "local",
            directory,
            new RecordingHandler(ClusterSendStatus.Accepted),
            messenger,
            () => now);
        var message = NewMessage(expiresAt: now.AddMinutes(1));

        var status = await router.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Same(message, Assert.Single(remoteHandler.Messages));
    }

    private static ClusterMessage NewMessage(
        DateTimeOffset expiresAt,
        string? correlationId = null,
        string? traceId = null,
        string? orderedBy = null)
    {
        return new ClusterMessage(
            "room/1",
            "command",
            new byte[] { 1, 2, 3 },
            expiresAt,
            "source",
            correlationId,
            traceId,
            orderedBy);
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

    private sealed class RecordingMessenger : INodeMessenger
    {
        private readonly ClusterSendStatus _status;

        public RecordingMessenger(ClusterSendStatus status)
        {
            _status = status;
        }

        public List<(RouteLocation Target, ClusterMessage Message)> Messages { get; } = new();

        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add((target, message));
            return ValueTask.FromResult(_status);
        }
    }

    private sealed class CountingRouteDirectory : IRouteDirectory
    {
        public int ResolveCount { get; private set; }

        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.Registered);
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return new ValueTask<RouteLocation?>((RouteLocation?)null);
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(0);
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.RouteNotFound);
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(0);
        }

        public ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(0);
        }
    }
}
