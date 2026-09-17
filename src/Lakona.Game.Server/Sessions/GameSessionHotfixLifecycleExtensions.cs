using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Sessions;

public static class GameSessionHotfixLifecycleExtensions
{
    public static IServiceCollection AddLakonaGameSessionHotfixLifecycle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<GameSessionLifecycleBindings>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IGameSessionLifecycleHandler,
            GameSessionHotfixLifecycleHandler>());

        return services;
    }
}
