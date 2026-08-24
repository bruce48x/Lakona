using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class MembershipProbeTargetSelectorTests
{
    [Fact]
    public void EveryNodeMonitorsOnlyThreePeersInALargeCluster()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var members = Enumerable.Range(1, 100)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var snapshot = new ClusterMembershipSnapshot(cluster, new MembershipViewId(1), members);

        foreach (var member in members)
        {
            var targets = MembershipProbeTargetSelector.Select(snapshot, member.Reference, 3);
            Assert.Equal(3, targets.Count);
            Assert.DoesNotContain(targets, target => target.Reference == member.Reference);
            Assert.Equal(3, targets.Select(static target => target.Reference).Distinct().Count());
        }
    }

    [Fact]
    public void JoiningMembersNeitherMonitorNorReceiveProbes()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var active = CreateMember(cluster, 1);
        var joining = new ClusterMember(
            new NodeReference(cluster, new NodeId("server-2"), new NodeIncarnationId(Guid.Parse("00000000-0000-0000-0000-000000000002"))),
            ClusterMemberState.Joining,
            new NodeEndpoint("tcp://127.0.0.1:21002"),
            labels: null);
        var snapshot = new ClusterMembershipSnapshot(cluster, new MembershipViewId(1), [active, joining]);

        Assert.Empty(MembershipProbeTargetSelector.Select(snapshot, active.Reference, 3));
        Assert.Empty(MembershipProbeTargetSelector.Select(snapshot, joining.Reference, 3));
    }

    private static ClusterMember CreateMember(ClusterIncarnationId cluster, int index) =>
        new(
            new NodeReference(
                cluster,
                new NodeId($"server-{index:000}"),
                new NodeIncarnationId(Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"))),
            ClusterMemberState.Active,
            new NodeEndpoint($"tcp://127.0.0.1:{21000 + index}"));
}
