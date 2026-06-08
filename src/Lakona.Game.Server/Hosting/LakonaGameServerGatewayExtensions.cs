using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameServerGatewayExtensions
{
    public static IServiceCollection AddLakonaGameServerGateway(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RpcServersHostedService>());
        return services;
    }

    [Obsolete("Register project-specific options directly and call AddLakonaGameServerGateway().")]
    public static IServiceCollection AddLakonaGameServerGateway(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddLakonaGameServerGateway();
    }

    public static IServiceCollection AddRpcServer<TConfigurator>(this IServiceCollection services)
        where TConfigurator : class, IRpcServerConfigurator
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcServerConfigurator, TConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RpcServersHostedService>());
        return services;
    }
}
