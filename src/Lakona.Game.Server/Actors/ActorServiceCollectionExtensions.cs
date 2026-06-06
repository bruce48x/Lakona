using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Actors;

public static class ActorServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerActors(
        this IServiceCollection services,
        Action<ActorRuntimeOptions>? configure = null)
    {
        var options = new ActorRuntimeOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(new LocalActorNodeIdentity("local"));
        services.TryAddSingleton<RemoteActorGateway>();
        services.TryAddSingleton<RemoteActorOptions>();
        services.TryAddSingleton<IActorRuntime, LakonaActorRuntime>();
        services.TryAddSingleton<IActorDirectory, InMemoryActorDirectory>();
        services.TryAddSingleton<IActorDirectoryCache, InMemoryActorDirectoryCache>();
        return services;
    }
}
