using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class NodeAdvertisementTests
{
    [Fact]
    public void AdvertisementAndMemberOwnImmutableCopiesOfCallerData()
    {
        var payload = new byte[] { 1, 2, 3 };
        var advertisement = new NodeAdvertisement("agar.realtime", "kcp-v1", payload);
        var advertisements = new List<NodeAdvertisement> { advertisement };
        var member = new ClusterMember(
            CreateReference(),
            ClusterMemberState.Recovering,
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            isVoter: true,
            labels: null,
            advertisements);

        payload[0] = 9;
        advertisements.Clear();

        Assert.Equal(new byte[] { 1, 2, 3 }, advertisement.Payload.ToArray());
        Assert.Same(advertisement, Assert.Single(member.Advertisements));
    }

    [Fact]
    public void AdvertisementLimitsRejectUnboundedMemberDescriptors()
    {
        Assert.Throws<ArgumentException>(() =>
            new NodeAdvertisement(" ", "kcp-v1", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NodeAdvertisement(
                "agar.realtime",
                "kcp-v1",
                new byte[NodeAdvertisementLimits.MaximumPayloadBytes + 1]));

        var advertisements = Enumerable.Range(0, NodeAdvertisementLimits.MaximumAdvertisementsPerMember + 1)
            .Select(index => new NodeAdvertisement($"kind-{index}", "v1", ReadOnlyMemory<byte>.Empty))
            .ToArray();

        Assert.Throws<ArgumentException>(() =>
            new ClusterMember(
                CreateReference(),
                ClusterMemberState.Recovering,
                new NodeEndpoint("tcp://127.0.0.1:21001"),
                isVoter: true,
                labels: null,
                advertisements));
    }

    [Fact]
    public void MemberOwnsCanonicalActorAndStartupDescriptors()
    {
        var actorHosts = new List<NodeActorHostDescriptor>
        {
            new("UserActor", "policy-b", "build-1"),
            new("RoomActor", "policy-a", "build-1")
        };
        var startupActors = new List<StartupActorDescriptor>
        {
            new("MatchmakingActor", "policy-b", "build-1"),
            new("LeaderboardActor", "policy-a", "build-1")
        };
        var member = new ClusterMember(
            CreateReference(),
            ClusterMemberState.Ready,
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            isVoter: true,
            labels: null,
            advertisements: null,
            actorHosts,
            startupActors);

        actorHosts.Clear();
        startupActors.Clear();

        Assert.Equal(
            new[] { "RoomActor", "UserActor" },
            member.ActorHosts.Select(descriptor => descriptor.Actor));
        Assert.Equal(
            new[] { "LeaderboardActor", "MatchmakingActor" },
            member.StartupActors.Select(descriptor => descriptor.Actor));
    }

    private static NodeReference CreateReference()
    {
        return new NodeReference(
            new ClusterIncarnationId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            new NodeId("data-1"),
            new NodeIncarnationId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
    }
}
