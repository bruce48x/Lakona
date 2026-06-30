using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Actors;

public static class ActorServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerActors(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ActorRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hosting = LakonaGameHostingOptions.FromConfiguration(configuration);
        return services.AddLakonaGameServerActors(options =>
        {
            hosting.Actors.ApplyTo(options);
            configure?.Invoke(options);
        });
    }

    public static IServiceCollection AddLakonaGameServerActors(
        this IServiceCollection services,
        Action<ActorRuntimeOptions>? configure = null)
    {
        var options = new ActorRuntimeOptions();
        configure?.Invoke(options);

        if (configure is null)
        {
            services.TryAddSingleton(options);
        }
        else
        {
            services.RemoveAll<ActorRuntimeOptions>();
            services.AddSingleton(options);
        }

        services.TryAddSingleton(new LocalActorNodeIdentity("local"));
        services.TryAddSingleton<RemoteActorGateway>();
        services.TryAddSingleton<RemoteActorOptions>();
        services.TryAddSingleton(provider => new LakonaActorRuntime(
            provider,
            provider.GetRequiredService<ActorRuntimeOptions>(),
            provider.GetServices<IActorDiagnosticsObserver>()));
        services.TryAddSingleton<IActorRuntime>(provider => provider.GetRequiredService<LakonaActorRuntime>());
        services.TryAddSingleton<IActorLifecycle>(provider => provider.GetRequiredService<LakonaActorRuntime>());
        services.TryAddSingleton<IActorDirectory, InMemoryActorDirectory>();
        services.TryAddSingleton<IActorDirectoryCache, InMemoryActorDirectoryCache>();
        return services;
    }
}
