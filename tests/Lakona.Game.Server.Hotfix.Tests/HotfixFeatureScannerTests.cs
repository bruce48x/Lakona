using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Scanning;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureScannerTests
{
    [Fact]
    public void Scanner_discovers_hotfix_feature_actor_tick_declarations()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(BattleRuntimeFeature).Assembly, [
            typeof(BattleRuntimeFeature)
        ]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var feature = Assert.Single(result.Features);
        Assert.Equal("battle-runtime", feature.Name);
        Assert.Equal(typeof(BattleRuntimeFeature), feature.FeatureType);

        var fixedTick = Assert.Single(feature.ActorTicks, tick => tick.Mode == HotfixActorTickMode.FixedActor);
        Assert.Equal(typeof(MatchmakingActor), fixedTick.ActorType);
        Assert.Equal("default", fixedTick.ActorId);
        Assert.Equal("TickAsync", fixedTick.MethodName);
        Assert.Equal(TimeSpan.FromMilliseconds(250), fixedTick.Interval);
        Assert.Equal(TickBacklogPolicy.Coalesce, fixedTick.BacklogPolicy);

        var activeTick = Assert.Single(feature.ActorTicks, tick => tick.Mode == HotfixActorTickMode.ActiveActors);
        Assert.Equal(typeof(RoomActor), activeTick.ActorType);
        Assert.Equal("", activeTick.ActorId);
        Assert.Equal("TickAsync", activeTick.MethodName);
        Assert.Equal(TimeSpan.FromMilliseconds(50), activeTick.Interval);
        Assert.Equal(TickBacklogPolicy.SkipIfPending, activeTick.BacklogPolicy);
    }

    [HotfixFeature("battle-runtime")]
    private sealed class BattleRuntimeFeature : HotfixGameFeature
    {
        public override void Configure(HotfixFeatureContext context)
        {
            context.ScheduleActorTick<MatchmakingActor>(
                "default",
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce);
            context.ScheduleActiveActorTicks<RoomActor>(
                TimeSpan.FromMilliseconds(50),
                TickBacklogPolicy.SkipIfPending);
        }
    }

    private sealed class MatchmakingActor
    {
    }

    private sealed class RoomActor
    {
    }
}
