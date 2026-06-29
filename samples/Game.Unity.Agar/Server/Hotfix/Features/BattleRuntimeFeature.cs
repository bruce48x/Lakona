using Agar.Sample.State.Rooms;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Features;

[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        context.Services.AddSingleton<IFeatureMessageHandler, BattleRuntimeFeatureMessageHandler>();
        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}
