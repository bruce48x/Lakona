using System.IO;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipWireCodecTests
{
    [Fact]
    public void NotLeaderResponseRoundTripsTheLeaderEndpoint()
    {
        var leaderEndpoint = new NodeEndpoint(
            "tcp://data-1:21001",
            new Dictionary<string, string> { ["region"] = "cn" });

        var decoded = MembershipWireCodec.DecodeNotLeaderResponse(
            MembershipWireCodec.EncodeNotLeaderResponse(leaderEndpoint));

        Assert.NotNull(decoded);
        Assert.Equal(leaderEndpoint.Address, decoded.Address);
        Assert.Equal("cn", decoded.Metadata["region"]);
    }

    [Fact]
    public void NotLeaderResponseWithoutEndpointRoundTripsAsNull()
    {
        var frame = MembershipWireCodec.EncodeNotLeaderResponse(null);

        Assert.True(MembershipWireCodec.IsNotLeaderResponse(frame));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(frame));
    }

    [Fact]
    public void NotLeaderResponseIsRejectedWhenTheKindDoesNotMatch()
    {
        var join = MembershipWireCodec.EncodeJoinRequest(
            new NodeId("gateway-1"),
            NodeIncarnationId.New(),
            new NodeEndpoint("tcp://gateway-1:21001"));

        Assert.False(MembershipWireCodec.IsNotLeaderResponse(join));
        Assert.Throws<InvalidDataException>(
            () => MembershipWireCodec.DecodeNotLeaderResponse(join));
    }

    [Fact]
    public void NotLeaderResponseRejectsTrailingData()
    {
        var frame = MembershipWireCodec.EncodeNotLeaderResponse(null);
        var payload = new byte[frame.Payload.Length + 1];
        frame.Payload.Span.CopyTo(payload);
        payload[^1] = 0xFF;

        Assert.Throws<InvalidDataException>(() =>
            MembershipWireCodec.DecodeNotLeaderResponse(
                new ClusterMembershipTransportFrame(payload)));
    }

    [Fact]
    public void MembershipUnavailableResponseIsATypedOutcome()
    {
        var frame = MembershipWireCodec.EncodeMembershipUnavailableResponse();

        Assert.True(MembershipWireCodec.IsMembershipUnavailableResponse(frame));
        Assert.False(MembershipWireCodec.IsNotLeaderResponse(frame));
        Assert.False(MembershipWireCodec.IsMembershipUnavailableResponse(
            MembershipWireCodec.EncodeNotLeaderResponse(null)));
    }
}
