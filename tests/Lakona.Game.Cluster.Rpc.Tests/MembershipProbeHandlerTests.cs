using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipProbeHandlerTests
{
    [Fact]
    public async Task ProbeRefreshesTheTableBeforeRejectingAnUnknownJoiningSource()
    {
        var setup = await CreateTwoNodesAsync(activateSecond: false);
        var handler = new MembershipProbeHandler(setup.FirstManager, setup.FirstState, new StubProbeTransport());

        var reply = await handler.HandleAsync(new MembershipProbeRequest
        {
            Cluster = setup.First.Cluster.Value,
            SourceNodeId = setup.Second.Node.Value,
            SourceIncarnation = setup.Second.Incarnation.Value,
            TargetNodeId = setup.First.Node.Value,
            TargetIncarnation = setup.First.Incarnation.Value,
            TargetEndpoint = "tcp://127.0.0.1:21001"
        }, TestContext.Current.CancellationToken);

        Assert.True(reply.IsAlive);
        Assert.True(setup.FirstState.Current.TryGetMember(setup.Second, out var joining));
        Assert.Equal(ClusterMemberState.Joining, joining!.State);
    }

    [Fact]
    public async Task GossipRefreshesBeforeValidatingANewActiveSource()
    {
        var setup = await CreateTwoNodesAsync(activateSecond: true);
        var staleView = setup.FirstState.Current.View;
        var table = await setup.FirstManager.ReadTableAsync(TestContext.Current.CancellationToken);
        var handler = new MembershipProbeHandler(setup.FirstManager, setup.FirstState, new StubProbeTransport());

        await handler.HandleGossipAsync(new MembershipGossipRequest
        {
            Cluster = setup.First.Cluster.Value,
            SourceNodeId = setup.Second.Node.Value,
            SourceIncarnation = setup.Second.Incarnation.Value,
            MembershipVersion = table.Version.Value
        }, TestContext.Current.CancellationToken);

        Assert.True(setup.FirstState.Current.View.CompareTo(staleView) > 0);
        Assert.True(setup.FirstState.Current.TryGetMember(setup.Second, out var active));
        Assert.Equal(ClusterMemberState.Active, active!.State);
    }

    private static async ValueTask<Setup> CreateTwoNodesAsync(bool activateSecond)
    {
        var table = new InMemoryMembershipTable();
        var firstState = new ClusterMembershipState();
        var firstManager = CreateManager(table, firstState, "server-1", 21001);
        var first = await firstManager.JoinAsync(TestContext.Current.CancellationToken);
        await firstManager.ActivateAsync(null, [], [], TestContext.Current.CancellationToken);

        var secondState = new ClusterMembershipState();
        var secondManager = CreateManager(table, secondState, "server-2", 21002);
        var second = await secondManager.JoinAsync(TestContext.Current.CancellationToken);
        if (activateSecond)
        {
            await secondManager.ActivateAsync(null, [], [], TestContext.Current.CancellationToken);
        }

        return new Setup(firstManager, firstState, first, second);
    }

    private static MembershipTableManager CreateManager(
        IMembershipTable table,
        ClusterMembershipState state,
        string nodeId,
        int port) =>
        new(
            new NodeId(nodeId),
            NodeIncarnationId.New(),
            new NodeEndpoint($"tcp://127.0.0.1:{port}"),
            new ClusterBuildTag("TestBuild1"),
            table,
            state);

    private sealed record Setup(
        MembershipTableManager FirstManager,
        ClusterMembershipState FirstState,
        NodeReference First,
        NodeReference Second);

    private sealed class StubProbeTransport : IMembershipProbeTransport
    {
        public ValueTask<bool> ProbeAsync(
            NodeReference source,
            ClusterMember target,
            NodeEndpoint contact,
            bool forward,
            CancellationToken cancellationToken = default) =>
            new(false);

        public ValueTask GossipAsync(
            NodeReference source,
            NodeEndpoint contact,
            MembershipViewId version,
            CancellationToken cancellationToken = default) =>
            default;
    }
}
