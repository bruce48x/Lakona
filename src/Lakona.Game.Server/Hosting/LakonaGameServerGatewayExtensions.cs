using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameServerGatewayExtensions
{
    public static IServiceCollection AddLakonaGameServerGateway(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ULinkRpcServersHostedService>());
        return services;
    }

    [Obsolete("Register project-specific options directly and call AddLakonaGameServerGateway().")]
    public static IServiceCollection AddLakonaGameServerGateway(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddLakonaGameServerGateway();
    }

    public static IServiceCollection AddULinkRpcServer<TConfigurator>(this IServiceCollection services)
        where TConfigurator : class, IULinkRpcServerConfigurator
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IULinkRpcServerConfigurator, TConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ULinkRpcServersHostedService>());
        return services;
    }
}
