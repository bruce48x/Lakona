using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Services;

namespace Agar.Sample.State;

public static class AgarSampleActorServiceCollectionExtensions
{
    public static IServiceCollection AddAgarSampleActors(this IServiceCollection services)
    {
        services.AddLakonaGameServerActors();
        services.TryAddSingleton<BattleRuntimeGatewayResolver>();
        return services;
    }
}
