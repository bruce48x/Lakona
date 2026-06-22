using Agar.Sample.State.Rooms;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Features;

[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}
