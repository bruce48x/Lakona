using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipSnapshotCodecTests
{
    [Fact]
    public void RoundTripPreservesDiscoveryAndTransportDescriptors()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("12345678-1111-2222-3333-123456789abc"));
        var snapshot = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(9),
            new[]
            {
                new ClusterMember(
                    new NodeReference(
                        cluster,
                        new NodeId("battle-1"),
                        new NodeIncarnationId(
                            Guid.Parse("abcdefab-1111-2222-3333-abcdefabcdef"))),
                    ClusterMemberState.Ready,
                    new NodeEndpoint(
                        "tcp://battle-1:21001",
                        new Dictionary<string, string> { ["tls"] = "required" }),
                    isVoter: true,
                    new Dictionary<string, string> { ["role"] = "battle" },
                    new[]
                    {
                        new NodeAdvertisement(
                            "agar.realtime",
                            "kcp-v1",
                            new byte[] { 1, 2, 3 })
                    },
                    new[]
                    {
                        new NodeActorHostDescriptor(
                            "RoomActor",
                            "room-policy",
                            "build-1",
                            new Dictionary<string, string> { ["capacity"] = "100" })
                    },
                    new[]
                    {
                        new StartupActorDescriptor(
                            "MatchmakingActor",
                            "match-policy",
                            "build-1",
                            new Dictionary<string, string> { ["region"] = "cn" })
                    })
            });

        var decoded = MembershipSnapshotCodec.Decode(
            MembershipSnapshotCodec.Encode(snapshot));

        var member = Assert.Single(decoded.Members);
        Assert.Equal("RoomActor", Assert.Single(member.ActorHosts).Actor);
        Assert.Equal("100", member.ActorHosts[0].Metadata["capacity"]);
        Assert.Equal("MatchmakingActor", Assert.Single(member.StartupActors).Actor);
        Assert.Equal("cn", member.StartupActors[0].Metadata["region"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(member.Advertisements).Payload.ToArray());
    }
}
