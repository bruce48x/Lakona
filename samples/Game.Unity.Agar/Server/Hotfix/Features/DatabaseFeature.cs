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