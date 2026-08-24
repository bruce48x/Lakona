using Lakona.Game.Cluster.Rpc;
using MemoryPack;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorDirectoryProtocolTests
{
    [Fact]
    public void SnapshotReplyRoundTripsItsDeclaredTotalCount()
    {
        var reply = new ActorDirectorySnapshotReply
        {
            Available = true,
            View = 17,
            Records = [new ActorDirectoryRecordDto { ActorId = "room/1" }],
            HasMore = false,
            TotalCount = 1
        };

        var bytes = MemoryPackSerializer.Serialize(reply);
        var roundTrip = MemoryPackSerializer.Deserialize<ActorDirectorySnapshotReply>(bytes);

        Assert.NotNull(roundTrip);
        Assert.Equal(1, roundTrip.TotalCount);
    }
}
