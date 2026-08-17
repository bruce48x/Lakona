using Lakona.Game.Cluster.Rpc;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterProtocolTests
{
    [Fact]
    public void Method_catalog_preserves_current_assignments_and_tombstones()
    {
        ClusterProtocolMethod[] expectedActive =
        [
            new("actor.ask", 2),
            new("actor.tell", 3),
            new("actor-location.lookup", 20),
            new("actor-location.register", 21),
            new("actor-location.unregister", 22),
            new("actor-location.registry-snapshot", 23),
            new("actor-lifecycle.create", 25),
            new("actor-lifecycle.destroy", 26),
            new("client-notification.dispatch", 30),
            new("client-notification.batch-dispatch", 31),
            new("startup-affinity.lookup", 32),
            new("startup-affinity.bind", 33),
            new("startup-affinity.catalog-lookup", 35),
            new("startup-affinity.retain", 36),
            new("startup-affinity.owner-snapshot", 37),
            new("membership.frame", 40)
        ];
        ClusterProtocolMethod[] expectedReserved =
        [
            new("cluster.send", 1),
            new("route.register", 10),
            new("route.resolve", 11),
            new("route.refresh-lease", 12),
            new("route.expire", 13),
            new("route.clear-by-node", 14),
            new("route.clear-by-node-epoch", 15),
            new("route.unregister", 16),
            new("actor-location.shard-snapshot", 24)
        ];

        Assert.Equal(
            expectedActive.OrderBy(method => method.Name, StringComparer.Ordinal),
            ClusterProtocol.Methods.Active.OrderBy(method => method.Name, StringComparer.Ordinal));
        Assert.Equal(
            expectedReserved.OrderBy(method => method.Name, StringComparer.Ordinal),
            ClusterProtocol.Methods.Reserved.OrderBy(method => method.Name, StringComparer.Ordinal));

        var all = ClusterProtocol.Methods.Active
            .Concat(ClusterProtocol.Methods.Reserved)
            .ToArray();
        Assert.Equal(all.Length, all.Select(method => method.Id).Distinct().Count());
        Assert.Equal(all.Length, all.Select(method => method.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Membership_codec_catalog_preserves_frame_and_version_domains()
    {
        ClusterProtocolFrameKind[] expectedFrames =
        [
            new("join.request", 1),
            new("join.response", 2),
            new("append.request", 3),
            new("append.response", 4),
            new("vote.request", 5),
            new("vote.response", 6),
            new("proof", 7),
            new("proof.response", 8),
            new("promote.request", 9),
            new("promote.response", 10),
            new("ready.request", 11),
            new("ready.response", 12),
            new("formation-probe.request", 13),
            new("formation-probe.response", 14),
            new("formation-agreement.request", 15),
            new("formation-agreement.response", 16),
            new("snapshot-install.request", 17),
            new("snapshot-install.response", 18),
            new("not-leader.response", 19),
            new("membership-unavailable.response", 20)
        ];

        Assert.Equal("lakona.cluster.memorypack.v4", ClusterProtocol.Identifier);
        Assert.Equal(1, ClusterProtocol.MembershipFrames.Version);
        Assert.Equal(2, ClusterProtocol.MembershipSnapshots.FormatVersion);
        Assert.Equal(
            expectedFrames.OrderBy(frame => frame.Name, StringComparer.Ordinal),
            ClusterProtocol.MembershipFrames.Active.OrderBy(frame => frame.Name, StringComparer.Ordinal));
        Assert.Equal(
            ClusterProtocol.MembershipFrames.Active.Count,
            ClusterProtocol.MembershipFrames.Active.Select(frame => frame.Id).Distinct().Count());
        Assert.Equal(
            ClusterProtocol.MembershipFrames.Active.Count,
            ClusterProtocol.MembershipFrames.Active.Select(frame => frame.Name).Distinct(StringComparer.Ordinal).Count());
    }
}
