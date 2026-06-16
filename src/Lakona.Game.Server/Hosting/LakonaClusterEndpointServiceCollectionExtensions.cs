using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Game.Server.Hosting;

public static class LakonaClusterEndpointServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameClusterEndpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var runtimeOptions = FindRuntimeOptions(services);
        if (runtimeOptions?.Cluster is null || string.IsNullOrWhiteSpace(runtimeOptions.Cluster.Endpoint))
        {
            return services;
        }

        services.TryAddSingleton<IClusterTransportFactory, TcpClusterTransportFactory>();
        services.TryAddSingleton<IRpcSerializer, JsonRpcSerializer>();
        services.TryAddSingleton<IClusterClientFactory>(provider => new ClusterClientFactory(
            provider.GetRequiredService<IClusterTransportFactory>(),
            provider.GetRequiredService<IRpcSerializer>()));
        services.TryAddSingleton<LocalClientNotificationCommandDispatcher>();
        services.TryAddSingleton<IClientNotificationRemoteDispatcher, ClusterClientNotificationDispatcher>();

        if (runtimeOptions.Cluster.Seeds.Count > 0)
        {
            var seed = runtimeOptions.Cluster.Seeds[0];
            services.TryAddSingleton<INodeDirectory>(provider => new SeededNodeDirectoryClient(
                provider.GetRequiredService<IClusterClientFactory>(),
                seed));
            services.TryAddSingleton<IRouteDirectory>(provider => new SeededRouteDirectoryClient(
                provider.GetRequiredService<IClusterClientFactory>(),
                seed));
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcServerConfigurator>(
            new LakonaClusterRpcServerConfigurator(runtimeOptions)));
        return services;
    }

    private static LakonaGameRuntimeOptions? FindRuntimeOptions(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(LakonaGameRuntimeOptions) &&
                descriptor.ImplementationInstance is LakonaGameRuntimeOptions options)
            {
                return options;
            }
        }

        return null;
    }
}
