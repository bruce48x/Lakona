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
        return services.AddLakonaGameServer(new LakonaGameHostingOptions());
    }

    public static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddLakonaGameServer(LakonaGameHostingOptions.FromConfiguration(configuration));
    }

    public static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        LakonaGameHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

        services.AddLakonaGameServerReliablePush();
        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGameSessionHotfixLifecycle();
        services.TryAddSingleton<ILakonaGameServer, LakonaGameServer>();
        return services;
    }
}
