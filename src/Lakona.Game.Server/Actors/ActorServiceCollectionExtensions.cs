using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Actors.Internal;
using Microsoft.Extensions.Hosting;

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
        services.TryAddSingleton<RemoteActorOptions>();
        services.TryAddSingleton<IActorLifecycleDispatcher, NoopActorLifecycleDispatcher>();
        services.TryAddSingleton<IActorHostClient, ActorHostClient>();
        services.TryAddSingleton<IStartupActorInvoker, StartupActorInvoker>();
        services.TryAddSingleton<ActorActivationRollbackRecorder>();
        services.TryAddSingleton<ActorCompensationLifetime>();
        services.TryAddSingleton(provider => new ActorActivationCatalog(
            provider,
            provider.GetRequiredService<ActorRuntimeOptions>(),
            provider.GetRequiredService<LocalActorNodeIdentity>(),
            provider.GetRequiredService<ActorActivationRollbackRecorder>(),
            provider.GetService<IActorDirectoryCache>(),
            provider.GetRequiredService<IActorLifecycleDispatcher>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<ActorActivationCatalog>>(),
            provider.GetRequiredService<ActorCompensationLifetime>()));
        services.TryAddSingleton<IActorActivationSnapshotSource>(provider =>
            provider.GetRequiredService<ActorActivationCatalog>());
        services.TryAddSingleton<IActorActivationLifecycle>(provider =>
            provider.GetRequiredService<ActorActivationCatalog>());
        services.TryAddSingleton<IActorRuntime>(provider => provider.GetRequiredService<ActorActivationCatalog>());
        services.TryAddSingleton<IActorActivationDispatcher>(provider => provider.GetRequiredService<ActorActivationCatalog>());
        services.TryAddSingleton<IActorPlacementService>(provider =>
            provider.GetRequiredService<ActorActivationCatalog>());
        services.TryAddSingleton<IActorSelfDeactivationSink>(provider =>
            provider.GetRequiredService<ActorActivationCatalog>());
        return services;
    }
}
