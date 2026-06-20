using Agar.Sample.State;
using Lakona.Game.Server.Features;

namespace Server.App.Features;

public sealed class StateStoreFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddAgarSampleActors();
    }
}
