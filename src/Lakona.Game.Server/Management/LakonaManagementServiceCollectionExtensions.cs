using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Management;

public static class LakonaManagementServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
