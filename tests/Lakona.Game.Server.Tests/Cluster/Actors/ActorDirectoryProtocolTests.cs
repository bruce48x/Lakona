using Lakona.Game.Cluster.Rpc;
using MemoryPack;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorDirectoryProtocolTests
{
    [Fact]
    public void ActivationSnapshotRequestRoundTripsItsStableSnapshotId()
    {
        var snapshotId = Guid.NewGuid();
        var request = new ActorDirectoryActivationSnapshotRequest
        {
            View = 17,
            Range = new ActorDirectoryRangeDto { Kind = 2 },
            Offset = 256,
            SnapshotId = snapshotId
        };

        var bytes = MemoryPackSerializer.Serialize(request);
        var roundTrip = MemoryPackSerializer.Deserialize<ActorDirectoryActivationSnapshotRequest>(bytes);

        Assert.NotNull(roundTrip);
        Assert.Equal(snapshotId, roundTrip.SnapshotId);
    }

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
