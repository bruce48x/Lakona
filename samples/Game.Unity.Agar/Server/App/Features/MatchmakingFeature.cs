using Microsoft.Extensions.DependencyInjection;
using Server.App.Hosting;
using Lakona.Game.Server.Features;

namespace Server.App.Features;

public sealed class MatchmakingFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddHostedService<MatchmakingHostedService>();
    }
}
