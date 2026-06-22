using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

public static class LakonaGameServerServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServer(this IServiceCollection services)
    {
        services.TryAddSingleton(new LakonaGameRuntimeOptions());
        return services.AddLakonaGameServer(new LakonaGameHostingOptions());
    }

    public static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(LakonaGameRuntimeOptions.FromConfiguration(configuration));
        return services.AddLakonaGameServer(
            LakonaGameHostingOptions.FromConfiguration(configuration),
            configuration);
    }

    public static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        LakonaGameHostingOptions options)
    {
        return services.AddLakonaGameServer(options, configuration: null);
    }

    private static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        LakonaGameHostingOptions options,
        IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(new LakonaGameRuntimeOptions());

        services.AddLakonaGameServerActors(actorOptions => options.Actors.ApplyTo(actorOptions));
        if (options.Sessions.Cleanup.Enabled)
        {
            services.AddLakonaGameServerSessionCleanup(sessionOptions => options.Sessions.Cleanup.ApplyTo(sessionOptions));
        }
        else
        {
            services.AddLakonaGameServerSessions();
            var sessionOptions = new SessionCleanupOptions();
            options.Sessions.Cleanup.ApplyTo(sessionOptions);
            services.RemoveAll<SessionCleanupOptions>();
            services.AddSingleton(sessionOptions);
        }

        if (configuration is null)
        {
            services.AddLakonaGameServerReliablePush();
        }
        else
        {
            services.AddLakonaGameServerReliablePush(configuration);
        }

        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGameSessionHotfixLifecycle();
        services.TryAddSingleton<IGameHandshakeService, GameHandshakeService>();
        services.TryAddSingleton<ILakonaGameServer, LakonaGameServer>();
        return services;
    }
}
