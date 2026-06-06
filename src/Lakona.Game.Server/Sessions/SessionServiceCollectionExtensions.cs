using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Sessions;

public static class SessionServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerSessions(this IServiceCollection services)
    {
        services.TryAddSingleton<IGameSessionDirectory, InMemoryGameSessionDirectory>();
        services.TryAddSingleton<IGameSessionResumeService, GameSessionResumeService>();
        services.TryAddSingleton<IGameSessionEndpointCloser, NoopGameSessionEndpointCloser>();
        return services;
    }

    public static IServiceCollection AddLakonaGameServerSessionCleanup(
        this IServiceCollection services,
        Action<SessionCleanupOptions>? configure = null)
    {
        services.AddLakonaGameServerSessions();

        var options = new SessionCleanupOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.AddHostedService<GameSessionCleanupHostedService>();
        return services;
    }
}
