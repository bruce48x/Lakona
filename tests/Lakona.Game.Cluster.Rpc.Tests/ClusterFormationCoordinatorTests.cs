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

        var nodes = await Task.WhenAll(
            formationA.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask(),
            formationB.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask(),
            formationC.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Single(nodes.Select(node => node.Membership.Current.Cluster).Distinct());
        Assert.Single(nodes, node => node.IsLeader);
        Assert.All(
            nodes,
            node => Assert.Contains(
                node.Membership.Current.Members,
                member => member.Reference.Node == node.Local.Node));
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
