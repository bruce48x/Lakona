using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Features;

[HotfixFeature("database")]
public sealed class DatabaseFeature : HotfixGameFeature
{
    public override bool Discoverable => false;

    public override void Configure(HotfixFeatureContext context)
    {
    }
}

[HotfixFeature("state-store")]
public sealed class StateStoreFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
    }
}

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);
    }
}

[HotfixFeature("leaderboard")]
public sealed class LeaderboardFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
    }
}
