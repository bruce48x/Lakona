using Microsoft.Extensions.DependencyInjection;
using Server.App.Realtime;
using Lakona.Game.Server.Features;

namespace Server.App.Features;

public sealed class BattleRuntimeFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddSingleton<RoomRuntimeHost>();
    }
}
