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

        var directorySeed = SelectRemoteDirectorySeed(runtimeOptions.Cluster);
        if (directorySeed is not null)
        {
            services.TryAddSingleton<INodeDirectory>(provider => new SeededNodeDirectoryClient(
                provider.GetRequiredService<IClusterClientFactory>(),
                directorySeed));
            services.TryAddSingleton<IRouteDirectory>(provider => new SeededRouteDirectoryClient(
                provider.GetRequiredService<IClusterClientFactory>(),
                directorySeed));
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcServerConfigurator>(
            new LakonaClusterRpcServerConfigurator(runtimeOptions)));
        return services;
    }

    private static string? SelectRemoteDirectorySeed(LakonaGameClusterOptions cluster)
    {
        if (cluster.Seeds.Count == 0)
        {
            return null;
        }

        foreach (var seed in cluster.Seeds)
        {
            if (!EndpointEquals(cluster.Endpoint, seed))
            {
                return seed;
            }
        }

        return null;
    }

    private static bool EndpointEquals(string left, string right)
    {
        try
        {
            var leftEndpoint = ClusterEndpoint.Parse(left);
            var rightEndpoint = ClusterEndpoint.Parse(right);
            return string.Equals(leftEndpoint.Scheme, rightEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(leftEndpoint.Host, rightEndpoint.Host, StringComparison.OrdinalIgnoreCase)
                && leftEndpoint.Port == rightEndpoint.Port
                && string.Equals(leftEndpoint.Path, rightEndpoint.Path, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
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
