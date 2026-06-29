using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Features;

[HotfixFeature("leaderboard")]
public sealed class LeaderboardFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
    }
}
