using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterNodeSenderTests
{
    [Fact]
    public async Task LocalSendUsesDiscoveredEndpoint()
    {
        var requestedNode = new NodeId("node-b");
        var discovery = new FixedDiscovery(
            new ClusterNodeDescriptor(
                requestedNode,
                NodeState.Ready,
                new Dictionary<string, NodeEndpoint>
                {
                    ["internal"] = new("tcp://node-b:21000")
                }));
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(
            discovery,
            messenger,
            new ClusterNodeSenderOptions { EndpointName = "internal" });

        var status = await sender.SendAsync(
            requestedNode,
            expectedNodeEpoch: null,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(requestedNode, messenger.LastTarget!.Node);
        Assert.Equal("tcp://node-b:21000", messenger.LastTarget.Endpoint.Address);
    }

    [Fact]
    public async Task LocalSendReturnsStaleRouteWhenNodeIsMissing()
    {
        var sender = new ClusterNodeSender(
            new FixedDiscovery(),
            new RecordingNodeMessenger());

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

    private static ClusterMessage CreateMessage() =>
        new(
            "room/42",
            "join",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddSeconds(5),
            new NodeId("node-a"));

    private sealed class FixedDiscovery(params ClusterNodeDescriptor[] nodes)
        : IClusterNodeDiscovery
    {
        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> QueryAsync(
            ClusterNodeDiscoveryQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ClusterNodeDescriptor>>(
                nodes.Where(query.Matches).ToArray());

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default) =>
            QueryAsync(new ClusterNodeDiscoveryQuery(labels: labels), cancellationToken);

        public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            var result = await ListAsync(labels, cancellationToken);
            return result.Count == 0 ? null : result[0];
        }
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
            return ValueTask.FromResult(ClusterSendStatus.Accepted);
        }
    }
}
