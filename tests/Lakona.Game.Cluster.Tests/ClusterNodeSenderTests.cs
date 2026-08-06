using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterNodeSenderTests
{
    [Fact]
    public async Task NodeSendUsesCommittedExactEndpoint()
    {
        var requestedNode = new NodeId("node-b");
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(new FixedMembership(Snapshot(requestedNode)), messenger);

        var status = await sender.SendAsync(
            requestedNode,
            expectedNodeEpoch: null,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(requestedNode, messenger.LastTarget!.NodeReference!.Node);
        Assert.Equal("tcp://node-b:21000", messenger.LastTarget.Endpoint.Address);
    }

    [Fact]
    public async Task LocalSendReturnsStaleRouteWhenNodeIsMissing()
    {
        var sender = new ClusterNodeSender(new FixedMembership(Snapshot()), new RecordingNodeMessenger());

        var status = await sender.SendAsync(
            new NodeId("node-b"),
            expectedNodeEpoch: null,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.StaleRoute, status);
    }

    [Fact]
    public async Task ExactSendUsesCommittedIncarnationAndView()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("77777777-1111-2222-3333-777777777777"));
        var target = new NodeReference(
            cluster,
            new NodeId("node-b"),
            new NodeIncarnationId(
                Guid.Parse("88888888-1111-2222-3333-888888888888")));
        var snapshot = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(6),
            [
                new ClusterMember(
                    target,
                    ClusterMemberState.Ready,
                    new NodeEndpoint("tcp://node-b:21000"),
                    isVoter: true)
            ]);
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(new FixedMembership(snapshot), messenger);

        var status = await sender.SendAsync(
            target,
            snapshot.View,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(target, messenger.LastTarget!.NodeReference);
        Assert.Equal(snapshot.View, messenger.LastTarget.MembershipView);
    }

    [Fact]
    public async Task ExactSendRejectsWrongMembershipViewWithoutSending()
    {
        var snapshot = Snapshot(new NodeId("node-b"));
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(new FixedMembership(snapshot), messenger);

        var status = await sender.SendAsync(
            snapshot.Members.Single().Reference,
            new MembershipViewId(snapshot.View.Value - 1),
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.StaleRoute, status);
        Assert.Null(messenger.LastTarget);
    }

    [Fact]
    public async Task ExactSendRejectsPreviousNodeIncarnationWithoutSending()
    {
        var snapshot = Snapshot(new NodeId("node-b"));
        var current = snapshot.Members.Single().Reference;
        var stale = new NodeReference(
            current.Cluster,
            current.Node,
            new NodeIncarnationId(Guid.Parse("99999999-1111-2222-3333-999999999999")));
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(new FixedMembership(snapshot), messenger);

        var status = await sender.SendAsync(
            stale,
            snapshot.View,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.StaleRoute, status);
        Assert.Null(messenger.LastTarget);
    }

    [Fact]
    public async Task ExactSendRejectsNonReadyMemberWithoutSending()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("77777777-1111-2222-3333-777777777777"));
        var target = new NodeReference(
            cluster,
            new NodeId("node-b"),
            new NodeIncarnationId(Guid.Parse("88888888-1111-2222-3333-888888888888")));
        var snapshot = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(6),
            [new ClusterMember(target, ClusterMemberState.Fenced, new NodeEndpoint("tcp://node-b:21000"), isVoter: true)]);
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(new FixedMembership(snapshot), messenger);

        var status = await sender.SendAsync(
            target,
            snapshot.View,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.StaleRoute, status);
        Assert.Null(messenger.LastTarget);
    }

    private static ClusterMessage CreateMessage() =>
        new(
            "room/42",
            "join",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddSeconds(5),
            new NodeId("node-a"));

    private static ClusterMembershipSnapshot Snapshot(params NodeId[] nodes)
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("77777777-1111-2222-3333-777777777777"));
        return new ClusterMembershipSnapshot(cluster, new MembershipViewId(6), nodes.Select((node, index) => new ClusterMember(
            new NodeReference(cluster, node, new NodeIncarnationId(Guid.Parse($"88888888-1111-2222-3333-{index + 1:000000000000}"))),
            ClusterMemberState.Ready, new NodeEndpoint($"tcp://{node.Value}:21000"), isVoter: true)).ToArray());
    }

    private sealed class FixedMembership(ClusterMembershipSnapshot current)
        : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingNodeMessenger : INodeMessenger
    {
        public RouteLocation? LastTarget { get; private set; }

        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            return new ValueTask<ClusterSendStatus>(ClusterSendStatus.Accepted);
        }
    }
}
