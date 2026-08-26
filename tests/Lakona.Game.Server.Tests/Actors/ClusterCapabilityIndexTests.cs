using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ClusterCapabilityIndexTests
{
    private static readonly ClusterIncarnationId Cluster = new(Guid.Parse("50000000-0000-0000-0000-000000000000"));

    [Fact]
    public void FindReadyActorHosts_reads_one_snapshot_filters_ready_and_sorts_ordinally()
    {
        var membership = new CountingMembership(Snapshot(
            Member("node-z", ClusterMemberState.Active, actor: "room"),
            Member("node-A", ClusterMemberState.Active, actor: "room"),
            Member("node-a", ClusterMemberState.Active, actor: "room"),
            Member("node-offline", ClusterMemberState.Joining, actor: "room"),
            Member("node-other", ClusterMemberState.Active, actor: "Room")));
        var index = new ClusterCapabilityIndex(membership);

        var matches = index.FindReadyActorHosts("room");

        Assert.Equal(1, membership.CurrentReads);
        Assert.Equal(["node-A", "node-a", "node-z"], matches.Select(static match => match.Node.Value));
    }

    [Fact]
    public void FindReadyStartupActors_reads_one_snapshot_and_matches_all_capability_parts_ordinally()
    {
        var membership = new CountingMembership(Snapshot(
            Member("node-z", ClusterMemberState.Active, startup: ("startup", "policy", "build")),
            Member("node-a", ClusterMemberState.Active, startup: ("startup", "policy", "build")),
            Member("node-A", ClusterMemberState.Active, startup: ("startup", "policy", "build")),
            Member("node-wrong-actor", ClusterMemberState.Active, startup: ("Startup", "policy", "build")),
            Member("node-wrong-policy", ClusterMemberState.Active, startup: ("startup", "Policy", "build")),
            Member("node-wrong-build", ClusterMemberState.Active, startup: ("startup", "policy", "Build")),
            Member("node-not-ready", ClusterMemberState.Joining, startup: ("startup", "policy", "build"))));
        var index = new ClusterCapabilityIndex(membership);

        var matches = index.FindReadyStartupActors("startup", "policy", "build");

        Assert.Equal(1, membership.CurrentReads);
        Assert.Equal(["node-A", "node-a", "node-z"], matches.Select(static match => match.Node.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void FindReadyActorHosts_rejects_blank_actor(string actor)
    {
        var index = new ClusterCapabilityIndex(new CountingMembership(Snapshot()));

        Assert.Throws<ArgumentException>(() => index.FindReadyActorHosts(actor));
    }

    [Theory]
    [InlineData("", "policy", "build")]
    [InlineData("startup", " ", "build")]
    [InlineData("startup", "policy", "")]
    public void FindReadyStartupActors_rejects_blank_capability_parts(string actor, string policyHash, string hotfixVersion)
    {
        var index = new ClusterCapabilityIndex(new CountingMembership(Snapshot()));

        Assert.Throws<ArgumentException>(() => index.FindReadyStartupActors(actor, policyHash, hotfixVersion));
    }

    [Fact]
    public void Find_methods_return_empty_when_no_ready_capability_matches()
    {
        var index = new ClusterCapabilityIndex(new CountingMembership(Snapshot(
            Member("node-a", ClusterMemberState.Joining, actor: "room", startup: ("startup", "policy", "build")))));

        Assert.Empty(index.FindReadyActorHosts("room"));
        Assert.Empty(index.FindReadyStartupActors("startup", "policy", "build"));
    }

    [Fact]
    public void Membership_snapshot_rejects_ambiguous_node_ids_before_capability_lookup()
    {
        var first = Member("node-a", ClusterMemberState.Active, actor: "room", incarnation: 1);
        var second = Member("node-a", ClusterMemberState.Active, actor: "room", incarnation: 2);

        Assert.Throws<ArgumentException>(() => new ClusterMembershipSnapshot(
            Cluster,
            new MembershipViewId(1),
            [first, second]));
    }

    private static ClusterMembershipSnapshot Snapshot(params ClusterMember[] members) =>
        new(Cluster, new MembershipViewId(1), members);

    private static ClusterMember Member(
        string node,
        ClusterMemberState state,
        string? actor = null,
        (string Actor, string Policy, string Build)? startup = null,
        int incarnation = 1) => new(
        new NodeReference(
            Cluster,
            new NodeId(node),
            new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000"))),
        state,
        new NodeEndpoint($"tcp://{node}:21000"),
        labels: null,
        actorHosts: actor is null ? [] : [new NodeActorHostDescriptor(actor, "policy", "build")],
        startupActors: startup is null ? [] : [new StartupActorDescriptor(startup.Value.Actor, startup.Value.Policy, startup.Value.Build)]);

    private sealed class CountingMembership(ClusterMembershipSnapshot snapshot) : IClusterMembership
    {
        private ClusterMembershipSnapshot current = snapshot;

        public int CurrentReads { get; private set; }

        public ClusterMembershipSnapshot Current
        {
            get
            {
                CurrentReads++;
                return current;
            }
            set => current = value;
        }

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default) => new(current);
    }
}
