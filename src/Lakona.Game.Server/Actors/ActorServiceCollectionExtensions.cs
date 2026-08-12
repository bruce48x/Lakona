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
        services.TryAddSingleton(provider => new LakonaActorRuntime(
            provider,
            provider.GetRequiredService<ActorRuntimeOptions>()));
        services.TryAddSingleton<IActorRuntime>(provider => provider.GetRequiredService<LakonaActorRuntime>());
        services.TryAddSingleton<IActorHostingRuntime>(provider => provider.GetRequiredService<LakonaActorRuntime>());
        services.TryAddSingleton<IActorLifecycleDispatcher, NoopActorLifecycleDispatcher>();
        services.TryAddSingleton<IActorPlacementService>(provider =>
            provider.GetRequiredService<ActorHosting>());
        services.TryAddSingleton<IActorHostClient, ActorHostClient>();
        services.TryAddSingleton<ActorLifecycleRpcHandler>();
        services.TryAddSingleton<IStartupActorAffinityDirectory, StartupActorAffinityDirectory>();
        services.TryAddSingleton<IStartupActorInvoker, StartupActorInvoker>();
        services.TryAddSingleton<ActorHostingRollbackRecorder>();
        services.TryAddSingleton<ActorActivationRegistry>();
        services.TryAddSingleton(provider => new ActorHosting(
            provider.GetRequiredService<IActorHostingRuntime>(),
            provider.GetRequiredService<LocalActorNodeIdentity>(),
            provider.GetRequiredService<ActorHostingRollbackRecorder>(),
            provider.GetService<IActorDirectory>(),
            provider.GetService<IActorDirectoryCache>(),
            provider.GetRequiredService<IActorLifecycleDispatcher>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<ActorHosting>>(),
            provider.GetRequiredService<ActorActivationRegistry>()));
        return services;
    }
}
