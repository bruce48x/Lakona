using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class InMemoryLoopbackNodeMessengerTests
{
    [Fact]
    public async Task RegisteredNodeReceivesMessage()
    {
        var messenger = new InMemoryLoopbackNodeMessenger();
        var handler = new RecordingHandler(ClusterSendStatus.Accepted);
        var message = NewMessage();
        messenger.RegisterNode("node-a", handler);

        var status = await messenger.SendAsync(NewLocation("node-a"), message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Same(message, Assert.Single(handler.Messages));
    }

    [Fact]
    public async Task MissingNodeReturnsHandlerUnavailable()
    {
        var messenger = new InMemoryLoopbackNodeMessenger();

        var status = await messenger.SendAsync(NewLocation("node-a"), NewMessage(), TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.HandlerUnavailable, status);
    }

    [Fact]
    public async Task HandlerBackpressureIsPreserved()
    {
        var messenger = new InMemoryLoopbackNodeMessenger();
        messenger.RegisterNode("node-a", new RecordingHandler(ClusterSendStatus.Backpressure));

        var status = await messenger.SendAsync(NewLocation("node-a"), NewMessage(), TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Backpressure, status);
    }

    private static ClusterMessage NewMessage()
    {
        return new ClusterMessage(
            "room/1",
            "command",
            new byte[] { 1 },
            DateTimeOffset.UtcNow.AddMinutes(1),
            "source");
    }

    private static RouteLocation NewLocation(NodeId node)
    {
        return new RouteLocation(
            "room/1",
            node,
            new NodeEndpoint("in-memory://" + node),
            DateTimeOffset.UtcNow.AddMinutes(1));
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
}
