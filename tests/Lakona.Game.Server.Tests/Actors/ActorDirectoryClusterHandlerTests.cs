using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorDirectoryClusterHandlerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task HandleAsync_missing_correlation_returns_rejected_without_reply(string? correlationId)
    {
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(new InMemoryActorDirectory(), sender);

        var status = await handler.HandleAsync(
            CreateMessage(
                ActorDirectoryClusterProtocol.ResolveKind,
                new ActorDirectoryRequest("user/player-1", null),
                correlationId),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Rejected, status);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task HandleAsync_missing_actor_id_sends_typed_error_reply()
    {
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(new InMemoryActorDirectory(), sender);

        var status = await handler.HandleAsync(
            CreateMessage(
                ActorDirectoryClusterProtocol.ResolveKind,
                new ActorDirectoryRequest(" ", null)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        var reply = AssertReply(sender, ActorDirectoryOperationStatus.InvalidRequest);
        Assert.False(string.IsNullOrWhiteSpace(reply.Error));
    }

    [Fact]
    public async Task HandleAsync_returns_reply_send_failure_unchanged()
    {
        var sender = new RecordingClusterNodeSender { Status = ClusterSendStatus.Failed };
        var handler = CreateHandler(new InMemoryActorDirectory(), sender);

        var status = await handler.HandleAsync(
            CreateMessage(
                ActorDirectoryClusterProtocol.ResolveKind,
                new ActorDirectoryRequest("user/player-1", null)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Failed, status);
        Assert.Equal(1, sender.SendCount);
    }

    [Theory]
    [InlineData(ActorDirectoryClusterProtocol.RegisterKind)]
    [InlineData(ActorDirectoryClusterProtocol.UnregisterKind)]
    public async Task HandleAsync_owner_command_without_owner_sends_typed_error_reply(string kind)
    {
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(new InMemoryActorDirectory(), sender);

        var status = await handler.HandleAsync(
            CreateMessage(kind, new ActorDirectoryRequest("user/player-1", null)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        var reply = AssertReply(sender, ActorDirectoryOperationStatus.InvalidRequest);
        Assert.False(string.IsNullOrWhiteSpace(reply.Error));
    }

    [Fact]
    public async Task HandleAsync_invalid_json_sends_typed_error_reply()
    {
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(new InMemoryActorDirectory(), sender);

        var status = await handler.HandleAsync(
            CreateMessage(ActorDirectoryClusterProtocol.ResolveKind, "not-json"u8.ToArray()),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        var reply = AssertReply(sender, ActorDirectoryOperationStatus.InvalidRequest);
        Assert.False(string.IsNullOrWhiteSpace(reply.Error));
    }

    [Fact]
    public async Task HandleAsync_registers_owner_and_replies_to_source()
    {
        var directory = new InMemoryActorDirectory();
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(directory, sender);

        var status = await handler.HandleAsync(
            CreateMessage(
                ActorDirectoryClusterProtocol.RegisterKind,
                new ActorDirectoryRequest("user/player-1", "battle-1")),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        AssertReply(sender, ActorDirectoryOperationStatus.Registered);
        var record = await directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.Equal(new NodeId("battle-1"), record.Node);
    }

    [Fact]
    public async Task HandleAsync_resolves_owner_and_replies_to_source()
    {
        var directory = new InMemoryActorDirectory();
        await directory.RegisterAsync(
            ActorId.From("user/player-1"),
            new NodeId("battle-1"),
            TestContext.Current.CancellationToken);
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(directory, sender);

        var status = await handler.HandleAsync(
            CreateMessage(
                ActorDirectoryClusterProtocol.ResolveKind,
                new ActorDirectoryRequest("user/player-1", null)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        var reply = AssertReply(sender, ActorDirectoryOperationStatus.Succeeded);
        Assert.NotNull(reply.Record);
        Assert.Equal("user/player-1", reply.Record.ActorId);
        Assert.Equal("battle-1", reply.Record.Node);
    }

    [Fact]
    public async Task HandleAsync_unregisters_owner_and_replies_to_source()
    {
        var directory = new InMemoryActorDirectory();
        await directory.RegisterAsync(
            ActorId.From("user/player-1"),
            new NodeId("battle-1"),
            TestContext.Current.CancellationToken);
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(directory, sender);

        var status = await handler.HandleAsync(
            CreateMessage(
                ActorDirectoryClusterProtocol.UnregisterKind,
                new ActorDirectoryRequest("user/player-1", "battle-1")),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        AssertReply(sender, ActorDirectoryOperationStatus.Unregistered);
        Assert.Null(await directory.ResolveAsync(
            ActorId.From("user/player-1"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Seeded_clients_share_directory_state_through_the_same_handler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sharedDirectory = new InMemoryActorDirectory();
        var replyRouter = new InProcessReplyRouter();
        var handler = CreateHandler(sharedDirectory, replyRouter);

        var gatewayGateway = new RemoteActorGateway();
        var battleGateway = new RemoteActorGateway();
        replyRouter.Add(new NodeId("gateway-1"), gatewayGateway.CreateReplyHandler());
        replyRouter.Add(new NodeId("battle-1"), battleGateway.CreateReplyHandler());

        var gatewayDirectory = new SeededActorDirectory(
            gatewayGateway,
            new InProcessSeedMessenger(handler),
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            "tcp://data-1:21001");
        var battleDirectory = new SeededActorDirectory(
            battleGateway,
            new InProcessSeedMessenger(handler),
            new LocalActorNodeIdentity(new NodeId("battle-1")),
            "tcp://data-1:21001");
        var actorId = ActorId.From("user/player-1");

        var register = await battleDirectory.RegisterAsync(
            actorId,
            new NodeId("battle-1"),
            cancellationToken);
        var resolved = await gatewayDirectory.ResolveAsync(actorId, cancellationToken);

        Assert.Equal(ActorDirectoryRegisterStatus.Registered, register);
        Assert.NotNull(resolved);
        Assert.Equal(new NodeId("battle-1"), resolved.Node);
    }

    [Fact]
    public async Task HandleAsync_unrelated_kind_returns_route_not_found_without_reply()
    {
        var sender = new RecordingClusterNodeSender();
        var handler = CreateHandler(new InMemoryActorDirectory(), sender);

        var status = await handler.HandleAsync(
            CreateMessage("unrelated", new ActorDirectoryRequest("user/player-1", null)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.RouteNotFound, status);
        Assert.Equal(0, sender.SendCount);
    }

    private static ActorDirectoryClusterHandler CreateHandler(
        IActorDirectory directory,
        IClusterNodeSender sender) => new(
            directory,
            sender,
            new LocalActorNodeIdentity(new NodeId("data-1")));

    private static ClusterMessage CreateMessage(
        string kind,
        ActorDirectoryRequest request,
        string? correlationId = "correlation-1") => new(
        ActorDirectoryClusterProtocol.Route,
        kind,
        JsonSerializer.SerializeToUtf8Bytes(request),
        DateTimeOffset.UtcNow.AddSeconds(5),
        new NodeId("gateway-1"),
        correlationId);

    private static ClusterMessage CreateMessage(string kind, ReadOnlyMemory<byte> payload) => new(
        ActorDirectoryClusterProtocol.Route,
        kind,
        payload,
        DateTimeOffset.UtcNow.AddSeconds(5),
        new NodeId("gateway-1"),
        "correlation-1");

    private static ActorDirectoryReply AssertReply(
        RecordingClusterNodeSender sender,
        ActorDirectoryOperationStatus expectedStatus)
    {
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(new NodeId("gateway-1"), sender.DestinationNode);
        Assert.NotNull(sender.Message);
        Assert.Equal("correlation-1", sender.Message.CorrelationId);

        var reply = JsonSerializer.Deserialize<ActorDirectoryReply>(sender.Message.Payload.Span);
        Assert.NotNull(reply);
        Assert.Equal(expectedStatus, reply.Status);
        return reply;
    }

    private sealed class RecordingClusterNodeSender : IClusterNodeSender
    {
        public ClusterSendStatus Status { get; set; } = ClusterSendStatus.Accepted;

        public int SendCount { get; private set; }

        public NodeId DestinationNode { get; private set; }

        public ClusterMessage? Message { get; private set; }

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            DestinationNode = nodeId;
            Message = message;
            return new ValueTask<ClusterSendStatus>(Status);
        }
    }

    private sealed class InProcessSeedMessenger(IClusterMessageHandler handler) : INodeMessenger
    {
        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            return handler.HandleAsync(message, cancellationToken);
        }
    }

    private sealed class InProcessReplyRouter : IClusterNodeSender
    {
        private readonly Dictionary<string, IClusterMessageHandler> _replyHandlers =
            new(StringComparer.Ordinal);

        public void Add(NodeId nodeId, IClusterMessageHandler replyHandler)
        {
            _replyHandlers.Add(nodeId.Value, replyHandler);
        }

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            return _replyHandlers.TryGetValue(nodeId.Value, out var replyHandler)
                ? replyHandler.HandleAsync(message, cancellationToken)
                : new ValueTask<ClusterSendStatus>(ClusterSendStatus.RouteNotFound);
        }
    }
}
