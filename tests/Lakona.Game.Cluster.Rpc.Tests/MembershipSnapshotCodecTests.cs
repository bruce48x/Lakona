using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipSnapshotCodecTests
{
    [Fact]
    public void EmptySnapshotMatchesVersionTwoGoldenBytes()
    {
        var snapshot = new ClusterMembershipSnapshot(
            new ClusterIncarnationId(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            new MembershipViewId(9),
            Array.Empty<ClusterMember>());

        var golden = new byte[]
        {
            0x02,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        Assert.Equal(golden, MembershipSnapshotCodec.Encode(snapshot));
        var decoded = MembershipSnapshotCodec.Decode(golden);
        Assert.Equal(snapshot.Cluster, decoded.Cluster);
        Assert.Equal(snapshot.View, decoded.View);
        Assert.Empty(decoded.Members);
    }

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
    }

    [Fact]
    public void EncodingIsStableAcrossMapInsertionOrder()
    {
        var first = CreateSnapshot(new Dictionary<string, string>
        {
            ["zone"] = "east",
            ["role"] = "battle"
        });
        var second = CreateSnapshot(new Dictionary<string, string>
        {
            ["role"] = "battle",
            ["zone"] = "east"
        });

        Assert.Equal(
            MembershipSnapshotCodec.Encode(first),
            MembershipSnapshotCodec.Encode(second));
    }

    [Fact]
    public void DecodeRejectsTrailingAndTruncatedPayloads()
    {
        var encoded = MembershipSnapshotCodec.Encode(CreateSnapshot(
            new Dictionary<string, string>()));
        var trailing = encoded.Append((byte)0xFF).ToArray();

        Assert.Throws<TerminalMembershipException>(() =>
            MembershipSnapshotCodec.Decode(trailing));
        Assert.Throws<TerminalMembershipException>(() =>
            MembershipSnapshotCodec.Decode(encoded.AsSpan(0, encoded.Length - 1)));
    }

    private static ClusterMembershipSnapshot CreateSnapshot(
        IReadOnlyDictionary<string, string> metadata)
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("12345678-1111-2222-3333-123456789abc"));
        return new ClusterMembershipSnapshot(
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
                    new NodeEndpoint("tcp://battle-1:21001", metadata),
                    isVoter: true)
            });
    }
}
