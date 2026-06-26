using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Services;

namespace Server.Hotfix.Features;

[HotfixFeature("state-store")]
public sealed class StateStoreFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        var services = GetServices(context);
        services.AddSingleton<MatchmakingNotifier>();
        services.AddSingleton<RoomNotifier>();
    }

    private static IServiceCollection GetServices(HotfixFeatureContext context)
    {
        return (IServiceCollection)(context.GetType().GetProperty("Services")?.GetValue(context)
            ?? throw new InvalidOperationException("Hotfix feature services are not available."));
    }
}
