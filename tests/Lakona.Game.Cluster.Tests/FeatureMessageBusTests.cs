using System.Text.Json;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class FeatureMessageBusTests
{
    [Fact]
    public async Task SendToFeatureReturnsFeatureNotFoundWhenNoReadyNodeExists()
    {
        var bus = new FeatureMessageBus(
            new EmptyDiscovery(),
            new ThrowingTransport(),
            new TestSerializer());

        var reply = await bus.SendToFeatureAsync<string, string>(
            new FeatureName("matchmaking"),
            "join",
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.FeatureNotFound, reply.Status);
    }

    [Fact]
    public async Task SendToFeatureUsesClusterEndpointOfSelectedNode()
    {
        var transport = new CapturingTransport();
        var bus = new FeatureMessageBus(
            new SingleNodeDiscovery(new ClusterNodeDescriptor(
                new NodeId("data-1"),
                NodeState.Ready,
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                {
                    ["cluster"] = new NodeEndpoint("tcp://10.0.0.1:21001")
                },
                [new NodeFeatureDescriptor("matchmaking")])),
            transport,
            new TestSerializer());

        await bus.SendToFeatureAsync<string, string>(
            new FeatureName("matchmaking"),
            "join",
            TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("data-1"), transport.LastNode);
        Assert.Equal("tcp://10.0.0.1:21001", transport.LastEndpoint);
        Assert.Equal("matchmaking", transport.LastRequest?.Feature.Value);
        Assert.Equal("join", transport.LastRequest?.Kind);
        Assert.Equal("\"join\"", System.Text.Encoding.UTF8.GetString(transport.LastRequest?.Payload.ToArray() ?? []));
    }

    [Fact]
    public async Task SendToNodeUsesExplicitTargetAndDoesNotQueryDiscovery()
    {
        var transport = new CapturingTransport();
        var target = new ClusterNodeDescriptor(
            new NodeId("runtime-7"),
            NodeState.Ready,
            new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
            {
                ["cluster"] = new NodeEndpoint("tcp://10.0.0.7:21001")
            },
            [new NodeFeatureDescriptor("battle-runtime")]);
        var bus = new FeatureMessageBus(
            new ThrowingDiscovery(),
            transport,
            new TestSerializer(),
            new NodeId("matchmaker-1"),
            TimeSpan.FromSeconds(12),
            () => new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero));

        var reply = await bus.SendToNodeAsync<CommandRequest, CommandReply>(
            target,
            new FeatureName("battle-runtime"),
            "17",
            new CommandRequest("room-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
        Assert.Equal(new NodeId("runtime-7"), transport.LastNode);
        Assert.Equal("tcp://10.0.0.7:21001", transport.LastEndpoint);
        Assert.Equal("battle-runtime", transport.LastRequest?.Feature.Value);
        Assert.Equal("17", transport.LastRequest?.Kind);
        Assert.Equal(new NodeId("matchmaker-1"), transport.LastRequest?.SourceNode);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 29, 8, 0, 12, TimeSpan.Zero),
            transport.LastRequest?.ExpiresAt);
        Assert.Equal("room-1", JsonSerializer.Deserialize<CommandRequest>(transport.LastRequest!.Payload.Span)!.RoomId);
    }

    [Fact]
    public async Task SendToNodeReturnsNodeUnavailableWhenExplicitTargetIsNotReady()
    {
        var bus = new FeatureMessageBus(
            new ThrowingDiscovery(),
            new ThrowingTransport(),
            new TestSerializer());
        var target = new ClusterNodeDescriptor(
            new NodeId("runtime-7"),
            NodeState.Starting,
            new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
            {
                ["cluster"] = new NodeEndpoint("tcp://10.0.0.7:21001")
            },
            [new NodeFeatureDescriptor("battle-runtime")]);

        var reply = await bus.SendToNodeAsync<CommandRequest, CommandReply>(
            target,
            new FeatureName("battle-runtime"),
            "17",
            new CommandRequest("room-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.NodeUnavailable, reply.Status);
    }

    [Fact]
    public async Task SendToNodeReturnsNodeUnavailableWhenExplicitTargetHasNoClusterEndpoint()
    {
        var bus = new FeatureMessageBus(
            new ThrowingDiscovery(),
            new ThrowingTransport(),
            new TestSerializer());
        var target = new ClusterNodeDescriptor(
            new NodeId("runtime-7"),
            NodeState.Ready,
            new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal),
            [new NodeFeatureDescriptor("battle-runtime")]);

        var reply = await bus.SendToNodeAsync<CommandRequest, CommandReply>(
            target,
            new FeatureName("battle-runtime"),
            "17",
            new CommandRequest("room-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.NodeUnavailable, reply.Status);
    }

    [Fact]
    public void FeatureCommandIdRejectsNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureCommandId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FeatureCommandId.From(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FeatureCommandId.From(-1));
    }

    [Fact]
    public void FeatureCommandIdFormatsUsingInvariantCulture()
    {
        var command = FeatureCommandId.From(42);

        Assert.Equal("42", command.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public void FeatureCommandIdTryParseRejectsInvalidWireValues(string value)
    {
        Assert.False(FeatureCommandId.TryParse(value, out var commandId));
        Assert.Equal(default, commandId);
    }

    [Fact]
    public void FeatureCommandIdTryParseAcceptsInvariantDecimalWireValue()
    {
        Assert.True(FeatureCommandId.TryParse("42", out var commandId));
        Assert.Equal(42, commandId.Value);
    }

    [Fact]
    public void FeatureMessageRequestAllowsBlankKindForWireLevelTypedRejection()
    {
        var request = new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            "",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("data-1"),
            "corr-1");

        Assert.Equal("", request.Kind);
    }

    [Fact]
    public void FeatureMessageRequestNormalizesNullKindForWireLevelTypedRejection()
    {
        var request = new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            null!,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("data-1"),
            "corr-1");

        Assert.Equal("", request.Kind);
    }

    [Fact]
    public void FeatureMessageReplyDeserializesAcceptedPayload()
    {
        var serializer = new TestSerializer();
        var reply = new FeatureMessageReply(
            ClusterSendStatus.Accepted,
            serializer.Serialize(new CommandReply("ok")));

        var payload = reply.GetPayload<CommandReply>(serializer);

        Assert.Equal("ok", payload.Status);
    }

    [Fact]
    public void FeatureMessageReplyRejectsPayloadReadWhenStatusIsNotAccepted()
    {
        var reply = new FeatureMessageReply(
            ClusterSendStatus.FeatureNotFound,
            Array.Empty<byte>(),
            "missing");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            reply.GetPayload<string>(new TestSerializer()));

        Assert.Contains("FeatureNotFound", exception.Message);
        Assert.Contains("missing", exception.Message);
    }

    private sealed class EmptyDiscovery : IClusterNodeDiscovery
    {
        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ClusterNodeDescriptor?> AnyAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ClusterNodeDescriptor?>((ClusterNodeDescriptor?)null);
        }
    }

    private sealed class SingleNodeDiscovery : IClusterNodeDiscovery
    {
        private readonly ClusterNodeDescriptor _node;

        public SingleNodeDiscovery(ClusterNodeDescriptor node)
        {
            _node = node;
        }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ClusterNodeDescriptor?> AnyAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ClusterNodeDescriptor?>(_node);
        }
    }

    private sealed class ThrowingDiscovery : IClusterNodeDiscovery
    {
        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Discovery should not be queried for node-pinned sends.");
        }

        public ValueTask<ClusterNodeDescriptor?> AnyAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Discovery should not be queried for node-pinned sends.");
        }
    }

    private sealed class CapturingTransport : IFeatureMessageTransport
    {
        public NodeId LastNode { get; private set; }

        public string LastEndpoint { get; private set; } = "";

        public FeatureMessageRequest? LastRequest { get; private set; }

        public ValueTask<FeatureMessageReply> SendAsync(
            ClusterNodeDescriptor target,
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastNode = target.Node;
            LastEndpoint = target.Endpoints["cluster"].Address;
            LastRequest = request;
            return new ValueTask<FeatureMessageReply>(
                new FeatureMessageReply(ClusterSendStatus.Accepted, Array.Empty<byte>()));
        }
    }

    private sealed class ThrowingTransport : IFeatureMessageTransport
    {
        public ValueTask<FeatureMessageReply> SendAsync(
            ClusterNodeDescriptor target,
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestSerializer : IFeatureMessageSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    private sealed record CommandRequest(string RoomId);

    private sealed record CommandReply(string Status);
}
