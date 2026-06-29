using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Features;

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);
    }
}
