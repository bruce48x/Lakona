using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hotfix;

internal static class HotfixActorTickServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameHotfixActorTicks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<HotfixActorTickScheduler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HotfixActorTickHostedService>());
        return services;
    }
}
