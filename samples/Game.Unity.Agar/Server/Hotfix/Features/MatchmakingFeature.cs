using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
    }
}
