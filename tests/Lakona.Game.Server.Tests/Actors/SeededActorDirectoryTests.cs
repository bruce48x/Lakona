using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class SeededActorDirectoryTests
{
    [Fact]
    public Task ResolveAsync_blank_record_actor_id_throws_unavailable()
    {
        return AssertMalformedRecordAsync(" ", "data-1");
    }

    [Fact]
    public Task ResolveAsync_blank_record_node_throws_unavailable()
    {
        return AssertMalformedRecordAsync("user/player-1", " ");
    }

    [Fact]
    public async Task ResolveAsync_reply_timeout_throws_unavailable_and_releases_pending()
    {
        var gateway = new RemoteActorGateway();
        var directory = new SeededActorDirectory(
            gateway,
            new StatusNodeMessenger(ClusterSendStatus.Accepted),
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            "tcp://10.0.0.1:21001",
            new RemoteActorOptions { DefaultTimeout = TimeSpan.FromMilliseconds(20) });

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task ResolveAsync_error_reply_throws_unavailable()
    {
        var gateway = new RemoteActorGateway();
        var messenger = new ReplyingNodeMessenger(gateway, new ActorDirectoryReply(
            ActorDirectoryOperationStatus.Succeeded,
            new ActorDirectoryRecordDto("user/player-1", "data-1", 7, DateTimeOffset.UtcNow),
            "seed failed"));
        var directory = CreateDirectory(gateway, messenger);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task ResolveAsync_preserves_caller_cancellation_without_pending_request()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var gateway = new RemoteActorGateway();
        var directory = CreateDirectory(
            gateway,
            new StatusNodeMessenger(ClusterSendStatus.Accepted));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            canceled.Token).AsTask());

        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task ResolveAsync_send_exception_releases_pending_and_throws_unavailable()
    {
        var gateway = new RemoteActorGateway();
        var directory = CreateDirectory(gateway, new ThrowingNodeMessenger());

        var exception = await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task ResolveAsync_non_accepted_send_releases_pending_and_throws_unavailable()
    {
        var gateway = new RemoteActorGateway();
        var directory = CreateDirectory(
            gateway,
            new StatusNodeMessenger(ClusterSendStatus.Backpressure));

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task ResolveAsync_translates_malformed_reply_to_unavailable()
    {
        var gateway = new RemoteActorGateway();
        var messenger = new PayloadReplyingNodeMessenger(gateway, "not-json"u8.ToArray());
        var directory = CreateDirectory(gateway, messenger);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task UnregisterAsync_maps_ownership_mismatch_reply()
    {
        var gateway = new RemoteActorGateway();
        var messenger = new ReplyingNodeMessenger(
            gateway,
            new ActorDirectoryReply(ActorDirectoryOperationStatus.OwnershipMismatch));
        var directory = CreateDirectory(gateway, messenger);

        var status = await directory.UnregisterAsync(
            ActorId.From("user/player-1"),
            new NodeId("data-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryUnregisterStatus.OwnershipMismatch, status);
        Assert.Equal(ActorDirectoryClusterProtocol.UnregisterKind, messenger.Message.Kind);
        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task RegisterAsync_maps_conflict_reply()
    {
        var gateway = new RemoteActorGateway();
        var messenger = new ReplyingNodeMessenger(
            gateway,
            new ActorDirectoryReply(ActorDirectoryOperationStatus.Conflict));
        var directory = CreateDirectory(gateway, messenger);

        var status = await directory.RegisterAsync(
            ActorId.From("user/player-1"),
            new NodeId("data-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryRegisterStatus.Conflict, status);
        Assert.Equal(ActorDirectoryClusterProtocol.RegisterKind, messenger.Message.Kind);
        Assert.Equal(0, gateway.PendingCount);
    }

    [Fact]
    public async Task ResolveAsync_sends_to_seed_endpoint_and_returns_owner()
    {
        var gateway = new RemoteActorGateway();
        var messenger = new ReplyingNodeMessenger(gateway, new ActorDirectoryReply(
            ActorDirectoryOperationStatus.Succeeded,
            new ActorDirectoryRecordDto("user/player-1", "data-1", 7, DateTimeOffset.UtcNow)));
        var directory = new SeededActorDirectory(
            gateway,
            messenger,
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            "tcp://10.0.0.1:21001");

        var record = await directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(record);
        Assert.Equal(new NodeId("data-1"), record.Node);
        Assert.Equal("tcp://10.0.0.1:21001", messenger.Target.Endpoint.Address);
        Assert.Equal(ActorDirectoryClusterProtocol.ResolveKind, messenger.Message.Kind);
        Assert.Equal(0, gateway.PendingCount);
    }

    private static SeededActorDirectory CreateDirectory(
        RemoteActorGateway gateway,
        INodeMessenger messenger) => new(
            gateway,
            messenger,
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            "tcp://10.0.0.1:21001");

    private static async Task AssertMalformedRecordAsync(string actorId, string node)
    {
        var gateway = new RemoteActorGateway();
        var messenger = new ReplyingNodeMessenger(gateway, new ActorDirectoryReply(
            ActorDirectoryOperationStatus.Succeeded,
            new ActorDirectoryRecordDto(actorId, node, 7, DateTimeOffset.UtcNow)));
        var directory = CreateDirectory(gateway, messenger);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.PendingCount);
    }

    private sealed class ReplyingNodeMessenger(
        RemoteActorGateway gateway,
        ActorDirectoryReply reply) : INodeMessenger
    {
        public RouteLocation Target { get; private set; } = default!;

        public ClusterMessage Message { get; private set; } = default!;

        public async ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            Target = target;
            Message = message;

            await gateway.CreateReplyHandler().HandleAsync(
                new ClusterMessage(
                    ClusterActorRouteKeys.ForReply(message.SourceNode),
                    RemoteActorGateway.ReplyKind,
                    JsonSerializer.SerializeToUtf8Bytes(reply),
                    DateTimeOffset.UtcNow.AddSeconds(5),
                    target.Node,
                    message.CorrelationId),
                cancellationToken);

            return ClusterSendStatus.Accepted;
        }
    }

    private sealed class PayloadReplyingNodeMessenger(
        RemoteActorGateway gateway,
        ReadOnlyMemory<byte> payload) : INodeMessenger
    {
        public async ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            await gateway.CreateReplyHandler().HandleAsync(
                new ClusterMessage(
                    ClusterActorRouteKeys.ForReply(message.SourceNode),
                    RemoteActorGateway.ReplyKind,
                    payload,
                    DateTimeOffset.UtcNow.AddSeconds(5),
                    target.Node,
                    message.CorrelationId),
                cancellationToken);

            return ClusterSendStatus.Accepted;
        }
    }

    private sealed class StatusNodeMessenger(ClusterSendStatus status) : INodeMessenger
    {
        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ClusterSendStatus>(status);
        }
    }

    private sealed class ThrowingNodeMessenger : INodeMessenger
    {
        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("send failed");
        }
    }
}
