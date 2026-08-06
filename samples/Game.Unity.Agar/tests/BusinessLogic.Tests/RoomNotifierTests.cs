using Server.Hotfix.Rooms;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RoomNotifierTests
{
    [Fact]
    public void SelectFramesAfterReturnsEveryMissingServerTickInOrder()
    {
        var frames = Enumerable.Range(91, 10)
            .Select(frame => new FrameSyncFrame
            {
                MatchId = "match-replay",
                Frame = frame
            })
            .Reverse()
            .ToArray();

        var missing = RoomNotifier
            .SelectFramesAfter(frames, lastReceivedServerTick: 95)
            .Select(static frame => frame.Frame)
            .ToArray();

        Assert.Equal([96, 97, 98, 99, 100], missing);
    }
}
