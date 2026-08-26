using Server.Hotfix.Rooms;
using Shared.Gameplay;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RoomNotifierTests
{
    [Fact]
    public void SelectFramesAfterKeepsCatchupContiguousAndBounded()
    {
        var history = Enumerable.Range(1, FrameSyncProtocol.RoundFrameCount)
            .Select(frame => new FrameSyncFrame
            {
                MatchId = "match-live",
                Frame = frame
            })
            .ToArray();

        var live = RoomNotifier.SelectFramesAfter(history, lastReceivedServerTick: 0);

        Assert.Equal(RoomNotifier.MaxFramesPerPush, live.Count);
        Assert.Equal(Enumerable.Range(1, RoomNotifier.MaxFramesPerPush), live.Select(static frame => frame.Frame));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void FramePublicationUsesBoundedBatchCadence(int frame, bool expected)
    {
        Assert.Equal(expected, RoomNotifier.ShouldPublishFrame(frame));
    }
}
