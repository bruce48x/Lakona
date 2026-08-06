using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterFormationCoordinatorTests
{
    [Fact]
    public async Task Single_node_forms_one_voter_cluster_without_a_bootstrap_role()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var formation = Create("data-1", endpoint, [], transport);
        transport.Register(endpoint, formation);

        var node = await formation.FormOrJoinAsync(TestContext.Current.CancellationToken);

        var member = Assert.Single(node.Membership.Current.Members);
        Assert.Equal("data-1", member.Reference.Node.Value);
        Assert.True(member.IsVoter);
        Assert.Equal(ClusterMemberState.Recovering, member.State);
    }

    [Fact]
    public async Task Connected_inconsistent_peer_hints_converge_during_concurrent_formation()
    {
        var transport = new InMemoryFormationTransport();
        var a = new ClusterFormationPeer(
            new NodeId("a"),
            new NodeEndpoint("tcp://127.0.0.1:21001"));
        var b = new ClusterFormationPeer(
            new NodeId("b"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));
        var c = new ClusterFormationPeer(
            new NodeId("c"),
            new NodeEndpoint("tcp://127.0.0.1:21003"));
        var formationA = Create(a.Node.Value, a.Endpoint, [b], transport);
        var formationB = Create(b.Node.Value, b.Endpoint, [a, c], transport);
        var formationC = Create(c.Node.Value, c.Endpoint, [b], transport);
        transport.Register(a.Endpoint, formationA);
        transport.Register(b.Endpoint, formationB);
        transport.Register(c.Endpoint, formationC);

        var nodeA = await formationA.FormOrJoinAsync(TestContext.Current.CancellationToken);
        // Membership leadership is acquired by the authority control loop, not on
        // ingress; the bootstrapped node must elect itself before peers can join.
        using var authorityCancellation = new CancellationTokenSource();
        var authorityLoop = nodeA.RunAsync(
            new NoopAuthorityListener(),
            transport,
            authorityCancellation.Token);
        await WaitUntilAsync(() => nodeA.IsLeader, TimeSpan.FromSeconds(2));

        var nodes = await Task.WhenAll(
            formationB.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask(),
            formationC.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask());

        var all = nodes.Append(nodeA).ToArray();
        Assert.Single(all.Select(node => node.Membership.Current.Cluster).Distinct());
        Assert.Single(all, node => node.IsLeader);
        Assert.All(
            all,
            node => Assert.Contains(
                node.Membership.Current.Members,
                member => member.Reference.Node == node.Local.Node));

        await authorityCancellation.CancelAsync();
        await authorityLoop;
    }

    [Fact]
    public async Task Unreachable_known_peer_never_shrinks_into_an_implicit_single_node_cluster()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var missing = new ClusterFormationPeer(
            new NodeId("missing"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));
        var formation = Create("data-1", endpoint, [missing], transport);
        transport.Register(endpoint, formation);

        await Assert.ThrowsAsync<AggregateException>(async () =>
            await formation.FormOrJoinAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Incomplete_formation_returns_retryable_not_leader_for_membership_ingress()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var formation = Create("data-1", endpoint, [], transport);

        var member = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));
        var frames = new[]
        {
            MembershipWireCodec.EncodeJoinRequest(
                new NodeId("gateway-2"), NodeIncarnationId.New(), new NodeEndpoint("tcp://127.0.0.1:21003")),
            MembershipWireCodec.EncodePromoteRequest(member.Local, member.Membership.Current.View, 1),
            MembershipWireCodec.EncodeReadyRequest(member.Membership.Current.Members[0])
        };

        foreach (var frame in frames)
        {
            var response = await formation.HandleAsync(frame, TestContext.Current.CancellationToken);
            Assert.True(MembershipWireCodec.IsNotLeaderResponse(response));
            Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(response));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The membership condition was not reached in time.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class NoopAuthorityListener : IClusterAuthorityListener
    {
        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
        }
    }

    private static ClusterFormationCoordinator Create(
        string node,
        NodeEndpoint endpoint,
        IReadOnlyList<ClusterFormationPeer> peers,
        IClusterMembershipTransport transport)
    {
        return new ClusterFormationCoordinator(
            new NodeId(node),
            endpoint,
            peers,
            transport,
            new ClusterMembershipNodeOptions
            {
                MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(2),
                JoinRetryWindow = TimeSpan.FromMilliseconds(100)
            });
    }

    private sealed class InMemoryFormationTransport : IClusterMembershipTransport
    {
        private readonly Dictionary<string, ClusterFormationCoordinator> nodes =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(NodeEndpoint endpoint, ClusterFormationCoordinator formation)
        {
            nodes.Add(endpoint.Address, formation);
        }

        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodes.TryGetValue(endpoint.Address, out var target))
            {
                throw new IOException("Peer is unreachable.");
            }

            return target.HandleAsync(request, cancellationToken);
        }
    }
}
