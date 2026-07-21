using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterNodeSenderTests
{
    [Fact]
    public async Task SendAsync_resolves_requested_node_and_sends_to_configured_endpoint()
    {
        var requestedNode = new NodeId("node-b");
        var directory = new StubNodeDirectory
        {
            Record = CreateNodeRecord(
                clusterName: "game",
                node: requestedNode,
                endpointName: "internal",
                endpoint: new NodeEndpoint("tcp://node-b:21000"),
                nodeEpoch: 7)
        };
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(
            directory,
            messenger,
            new ClusterNodeSenderOptions { ClusterName = "game", EndpointName = "internal" });
        var route = ClusterActorRouteKeys.ForActor("room/42");
        var message = new ClusterMessage(
            route,
            "join",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddSeconds(5),
            new NodeId("node-a"));

        var status = await sender.SendAsync(
            requestedNode,
            expectedNodeEpoch: 7,
            route,
            message,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal("game", directory.LastClusterName);
        Assert.Equal(requestedNode, directory.LastNode);
        Assert.NotNull(messenger.LastTarget);
        Assert.Equal(requestedNode, messenger.LastTarget.Node);
        Assert.Equal(7, messenger.LastTarget.NodeEpoch);
        Assert.Equal(route, messenger.LastTarget.Route);
        Assert.Equal("tcp://node-b:21000", messenger.LastTarget.Endpoint.Address);
        Assert.Same(message, messenger.LastMessage);
    }

    [Fact]
    public async Task SendAsync_returns_stale_route_when_node_is_missing()
    {
        var sender = new ClusterNodeSender(
            new StubNodeDirectory(),
            new RecordingNodeMessenger(),
            new ClusterNodeSenderOptions());

        var status = await sender.SendAsync(
            new NodeId("node-b"),
            expectedNodeEpoch: 7,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.StaleRoute, status);
    }

    [Fact]
    public async Task SendAsync_returns_handler_unavailable_when_configured_endpoint_is_missing()
    {
        var sender = new ClusterNodeSender(
            new StubNodeDirectory
            {
                Record = CreateNodeRecord(
                    clusterName: "local",
                    node: new NodeId("node-b"),
                    endpointName: "client",
                    endpoint: new NodeEndpoint("tcp://node-b:20000"),
                    nodeEpoch: 1)
            },
            new RecordingNodeMessenger(),
            new ClusterNodeSenderOptions { EndpointName = "cluster" });

        var status = await sender.SendAsync(
            new NodeId("node-b"),
            expectedNodeEpoch: 1,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.HandlerUnavailable, status);
    }

    [Fact]
    public async Task SendAsync_rejects_expected_epoch_mismatch_without_dispatching()
    {
        var messenger = new RecordingNodeMessenger();
        var sender = new ClusterNodeSender(
            new StubNodeDirectory
            {
                Record = CreateNodeRecord(
                    clusterName: "local",
                    node: new NodeId("node-b"),
                    endpointName: "cluster",
                    endpoint: new NodeEndpoint("tcp://node-b:21000"),
                    nodeEpoch: 8)
            },
            messenger,
            new ClusterNodeSenderOptions());

        var status = await sender.SendAsync(
            new NodeId("node-b"),
            expectedNodeEpoch: 7,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.NodeEpochMismatch, status);
        Assert.Null(messenger.LastMessage);
    }

    [Fact]
    public async Task SendAsync_returns_messenger_status()
    {
        var messenger = new RecordingNodeMessenger
        {
            Status = ClusterSendStatus.Backpressure
        };
        var sender = new ClusterNodeSender(
            new StubNodeDirectory
            {
                Record = CreateNodeRecord(
                    clusterName: "local",
                    node: new NodeId("node-b"),
                    endpointName: "cluster",
                    endpoint: new NodeEndpoint("tcp://node-b:21000"),
                    nodeEpoch: 1)
            },
            messenger,
            new ClusterNodeSenderOptions());

        var status = await sender.SendAsync(
            new NodeId("node-b"),
            expectedNodeEpoch: null,
            "room/42",
            CreateMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Backpressure, status);
    }

    [Fact]
    public async Task ExactSendUsesCommittedIncarnationAndViewWithoutDirectoryLookup()
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
            new[]
            {
                new ClusterMember(
                    target,
                    ClusterMemberState.Ready,
                    new NodeEndpoint("tcp://node-b:21000"),
                    isVoter: true)
            });
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
        Assert.Equal("tcp://node-b:21000", messenger.LastTarget.Endpoint.Address);
    }

    private static ClusterMessage CreateMessage()
    {
        return new ClusterMessage(
            "room/42",
            "join",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddSeconds(5),
            new NodeId("node-a"));
    }

    private static NodeRecord CreateNodeRecord(
        string clusterName,
        NodeId node,
        string endpointName,
        NodeEndpoint endpoint,
        long nodeEpoch)
    {
        return new NodeRecord(
            clusterName,
            node,
            nodeEpoch,
            new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
            {
                [endpointName] = endpoint
            },
            actorHosts: [],
            labels: null,
            NodeState.Ready,
            DateTimeOffset.UtcNow.AddMinutes(5),
            DateTimeOffset.UtcNow);
    }

    private sealed class StubNodeDirectory : INodeDirectory
    {
        public NodeRecord? Record { get; init; }

        public string? LastClusterName { get; private set; }

        public NodeId LastNode { get; private set; }

        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            LastClusterName = clusterName;
            LastNode = node;
            return ValueTask.FromResult(Record);
        }

        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedMembership : IClusterMembership
    {
        public FixedMembership(ClusterMembershipSnapshot current)
        {
            Current = current;
        }

        public ClusterMembershipSnapshot Current { get; }

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingNodeMessenger : INodeMessenger
    {
        public RouteLocation? LastTarget { get; private set; }

        public ClusterMessage? LastMessage { get; private set; }

        public ClusterSendStatus Status { get; init; } = ClusterSendStatus.Accepted;

        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            LastMessage = message;
            return ValueTask.FromResult(Status);
        }
    }
}
