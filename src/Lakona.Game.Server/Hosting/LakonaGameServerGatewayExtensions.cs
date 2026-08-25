using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameServerGatewayExtensions
{
    public static IServiceCollection AddLakonaGameServerGateway(this IServiceCollection services)
    {
        services.TryAddSingleton<RpcServersHostedService>();
        return services;
    }

    public static IServiceCollection AddRpcServer<TConfigurator>(this IServiceCollection services)
        where TConfigurator : class, IRpcServerConfigurator
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcServerConfigurator, TConfigurator>());
        services.TryAddSingleton<RpcServersHostedService>();
        return services;
    }
}
