using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Sessions;

public static class GameSessionHotfixLifecycleExtensions
{
    public static IServiceCollection AddLakonaGameSessionHotfixLifecycle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHotfixRequiredServiceContracts,
            GameSessionHotfixLifecycleRequiredContracts>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IGameSessionLifecycleHandler,
            GameSessionHotfixLifecycleHandler>());

        return services;
    }
}
