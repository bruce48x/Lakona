using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterMembershipSnapshotTests
{
    [Fact]
    public void SnapshotCopiesCanonicalizesAndResolvesExactNodeReferences()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var nodeA = new NodeReference(
            cluster,
            new NodeId("data-a"),
            new NodeIncarnationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        var nodeB = new NodeReference(
            cluster,
            new NodeId("data-b"),
            new NodeIncarnationId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")));
        var members = new List<ClusterMember>
        {
            CreateMember(nodeB, "tcp://127.0.0.1:21002"),
            CreateMember(nodeA, "tcp://127.0.0.1:21001")
        };

        var snapshot = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(7),
            members);
        members.Clear();

        Assert.Equal(2, snapshot.Members.Count);
        Assert.Equal(nodeA, snapshot.Members[0].Reference);
        Assert.Equal(nodeB, snapshot.Members[1].Reference);
        Assert.True(snapshot.TryGetMember(nodeA, out var resolved));
        Assert.Equal("tcp://127.0.0.1:21001", resolved!.ClusterEndpoint.Address);

        var restartedNodeA = new NodeReference(
            cluster,
            new NodeId("data-a"),
            new NodeIncarnationId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));
        Assert.False(snapshot.TryGetMember(restartedNodeA, out _));
    }

    [Fact]
    public async Task MembershipWaitersObserveOnlyANewerCommittedSnapshot()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var initial = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(3),
            Array.Empty<ClusterMember>());
        var next = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(4),
            Array.Empty<ClusterMember>());
        var state = new ClusterMembershipState(initial);
        IClusterMembership membership = state;

        var cancellationToken = TestContext.Current.CancellationToken;
        var first = membership.WaitForChangeAsync(initial.View, cancellationToken).AsTask();
        var second = membership.WaitForChangeAsync(initial.View, cancellationToken).AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        state.Publish(next);

        Assert.Same(next, await first);
        Assert.Same(next, await second);
        Assert.Same(next, membership.Current);
        Assert.Same(next, await membership.WaitForChangeAsync(initial.View, cancellationToken));
    }

    [Fact]
    public void ExplicitBootstrapPublishesOneRecoveringVoterAndCannotRunTwice()
    {
        var runtime = new ClusterMembershipRuntime();
        var nodeIncarnation = new NodeIncarnationId(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        runtime.BootstrapNewCluster(
            new NodeId("data-1"),
            nodeIncarnation,
            new NodeEndpoint("tcp://127.0.0.1:21001"));

        IClusterMembership membership = runtime;
        var snapshot = membership.Current;
        var member = Assert.Single(snapshot.Members);
        Assert.NotEqual(Guid.Empty, snapshot.Cluster.Value);
        Assert.Equal(new MembershipViewId(1), snapshot.View);
        Assert.Equal(new NodeId("data-1"), member.Reference.Node);
        Assert.Equal(nodeIncarnation, member.Reference.Incarnation);
        Assert.Equal(ClusterMemberState.Recovering, member.State);
        Assert.True(member.IsVoter);
        Assert.Throws<InvalidOperationException>(() =>
        {
            runtime.BootstrapNewCluster(
                new NodeId("data-1"),
                nodeIncarnation,
                new NodeEndpoint("tcp://127.0.0.1:21001"));
        });
    }

    [Fact]
    public async Task PublishingACommittedReadyViewMakesItObservable()
    {
        var runtime = new ClusterMembershipRuntime();
        runtime.BootstrapNewCluster(
            new NodeId("data-1"),
            new NodeIncarnationId(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")),
            new NodeEndpoint("tcp://127.0.0.1:21001"));
        IClusterMembership membership = runtime;
        var recovering = membership.Current;
        var changed = membership.WaitForChangeAsync(
            recovering.View,
            TestContext.Current.CancellationToken).AsTask();

        var recoveringMember = Assert.Single(recovering.Members);
        runtime.PublishCommitted(new ClusterMembershipSnapshot(
            recovering.Cluster,
            new MembershipViewId(2),
            new[]
            {
                new ClusterMember(
                    recoveringMember.Reference,
                    ClusterMemberState.Ready,
                    recoveringMember.ClusterEndpoint,
                    recoveringMember.IsVoter,
                    recoveringMember.Labels,
                    recoveringMember.Advertisements,
                    recoveringMember.ActorHosts,
                    recoveringMember.StartupActors)
            }));

        var ready = await changed;
        var member = Assert.Single(ready.Members);
        Assert.Equal(new MembershipViewId(2), ready.View);
        Assert.Equal(ClusterMemberState.Ready, member.State);
        Assert.Equal(recovering.Cluster, ready.Cluster);
        Assert.Equal(
            recoveringMember.Reference,
            member.Reference);
    }

    private static ClusterMember CreateMember(NodeReference reference, string endpoint)
    {
        return new ClusterMember(
            reference,
            ClusterMemberState.Ready,
            new NodeEndpoint(endpoint),
            isVoter: true);
    }
}
