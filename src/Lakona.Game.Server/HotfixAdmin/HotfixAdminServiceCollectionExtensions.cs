using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.LocalAdmin;

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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, HotfixAdminStatusRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, HotfixAdminActivateRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, HotfixAdminRollbackRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, HotfixAdminReloadRoute>());
        return services;
    }
}
