using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.Hotfix;

public static class HotfixServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameHotfix(
        this IServiceCollection services,
        IHotfixAssemblySource source,
        IEnumerable<string>? sharedAssemblyNames = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);
        var sharedNames = (sharedAssemblyNames ?? Array.Empty<string>()).ToArray();
        services.RemoveAll<IHotfixAssemblySource>();
        services.RemoveAll<IHotfixManager>();
        services.AddSingleton(source);
        services.TryAddSingleton<IHotfixServiceInvoker, HotfixServiceInvoker>();
        services.AddSingleton<IHotfixManager>(provider =>
            new HotfixManager(provider.GetRequiredService<IHotfixAssemblySource>(), sharedNames));
        return services;
    }

    public static IServiceCollection AddLakonaGameHotfixFileWatcher(
        this IServiceCollection services,
        Action<HotfixFileWatcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<HotfixFileWatcherOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHostedService<HotfixFileWatcherHostedService>();
        return services;
    }
}
