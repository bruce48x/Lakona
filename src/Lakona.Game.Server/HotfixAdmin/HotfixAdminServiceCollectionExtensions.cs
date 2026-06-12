using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Hotfix;

namespace Lakona.Game.Server.HotfixAdmin;

public static class HotfixAdminServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameHotfixAdmin(
        this IServiceCollection services,
        Action<HotfixAdminOptions>? configure = null)
    {
        var options = new HotfixAdminOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(sp => new HotfixVersionStore(options.HotfixRoot));
        services.AddSingleton<HotfixAdminController>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HotfixAdminHostedService>());
        return services;
    }
}
