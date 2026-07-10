using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hosting;

public static class LakonaClusterEndpointServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameClusterEndpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var runtimeOptions = FindRuntimeOptions(services) ?? new LakonaGameRuntimeOptions();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(LakonaGameRuntimeOptions)))
        {
            services.AddSingleton(runtimeOptions);
        }

        services.TryAddSingleton<IClusterTransportFactory, TcpClusterTransportFactory>();
        services.RemoveAll<IRpcSerializer>();
        services.AddSingleton(_ => new LakonaClusterRpcSerializer(
            LakonaEndpointRuntimeDefaults.CreateClusterSerializer(runtimeOptions.Cluster)));
        services.AddSingleton<IRpcSerializer>(provider =>
            provider.GetRequiredService<LakonaClusterRpcSerializer>().Serializer);
        services.TryAddSingleton<IRemoteActorSerializer>(provider =>
            new RpcRemoteActorSerializer(provider.GetRequiredService<LakonaClusterRpcSerializer>().Serializer));
        services.TryAddSingleton<IClusterClientFactory>(provider => new ClusterClientFactory(
            provider.GetRequiredService<IClusterTransportFactory>(),
            provider.GetRequiredService<LakonaClusterRpcSerializer>().Serializer));
        services.TryAddSingleton<ClusterLocalMessageHandler>();
        services.TryAddSingleton<INodeMessenger, ClusterNodeMessenger>();
        services.TryAddSingleton<IClusterNodeSender, ClusterNodeSender>();
        services.TryAddSingleton<LocalClientNotificationCommandDispatcher>();
        services.TryAddSingleton<IClientNotificationRemoteDispatcher, ClusterClientNotificationDispatcher>();

        TryAddConfiguredNodeDirectory(services, runtimeOptions.Cluster);

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
        else
        {
            services.TryAddSingleton<INodeDirectory, InMemoryNodeDirectory>();
            services.TryAddSingleton<IRouteDirectory, InMemoryRouteDirectory>();
        }

        var hasActorRuntime = services.Any(
            static descriptor => descriptor.ServiceType == typeof(IActorRuntime));
        RemoveActorDirectoryHandlerDescriptors(services);
        if (hasActorRuntime && directorySeed is null)
        {
            if (services.Any(static descriptor =>
                    descriptor.ServiceType == typeof(SeededActorDirectory)))
            {
                services.RemoveAll<SeededActorDirectory>();
                services.RemoveAll<IActorDirectory>();
                services.AddSingleton<IActorDirectory, InMemoryActorDirectory>();
            }

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IClusterMessageHandler, ActorDirectoryClusterHandler>());
        }
        else if (hasActorRuntime && directorySeed is not null)
        {
            services.RemoveAll<SeededActorDirectory>();
            services.RemoveAll<IActorDirectory>();
            services.AddSingleton(provider => new SeededActorDirectory(
                provider.GetRequiredService<RemoteActorGateway>(),
                provider.GetRequiredService<INodeMessenger>(),
                provider.GetRequiredService<LocalActorNodeIdentity>(),
                directorySeed));
            services.AddSingleton<IActorDirectory>(provider =>
                provider.GetRequiredService<SeededActorDirectory>());
        }

        services.TryAddSingleton<IClusterRouter>(provider => new ClusterRouter(
            new NodeId(runtimeOptions.Node.Id),
            provider.GetRequiredService<IRouteDirectory>(),
            provider.GetRequiredService<ClusterLocalMessageHandler>(),
            provider.GetRequiredService<INodeMessenger>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClusterMessageHandler, HotfixActorClusterHandler>());
        services.TryAddSingleton<IRemoteActorInvoker>(provider => new RemoteActorInvoker(
            provider.GetRequiredService<RemoteActorGateway>(),
            provider.GetRequiredService<LocalActorNodeIdentity>().NodeId,
            provider.GetRequiredService<IClusterNodeSender>(),
            provider.GetService<RemoteActorOptions>()));
        services.TryAddSingleton<IClusterNodeDiscovery, ClusterNodeDiscovery>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcServerConfigurator>(
            new LakonaClusterRpcServerConfigurator(runtimeOptions)));
        return services;
    }

    private static void RemoveActorDirectoryHandlerDescriptors(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IClusterMessageHandler) &&
                descriptor.ImplementationType == typeof(ActorDirectoryClusterHandler))
            {
                services.RemoveAt(index);
            }
        }
    }

    private static void TryAddConfiguredNodeDirectory(
        IServiceCollection services,
        LakonaGameClusterOptions cluster)
    {
        var directory = cluster.Directory;
        if (!string.Equals(directory.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        services.TryAddSingleton(provider =>
        {
            var configuration = provider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var connectionString = configuration?.GetConnectionString(directory.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Lakona cluster directory provider 'postgres' requires ConnectionStrings:{directory.ConnectionStringName}.");
            }

            return new SqlNodeDirectoryOptions(
                () => new ValueTask<DbConnection>(CreatePostgresConnection(connectionString)),
                SqlNodeDirectoryDialect.Postgres,
                directory.NodeTable);
        });
        services.TryAddSingleton<INodeDirectory, SqlNodeDirectory>();
    }

    private static DbConnection CreatePostgresConnection(string connectionString)
    {
        var type = Type.GetType("Npgsql.NpgsqlConnection, Npgsql", throwOnError: false);
        if (type is null)
        {
            throw new InvalidOperationException(
                "The Npgsql package is required when Lakona:Cluster:Directory:Provider is 'postgres'.");
        }

        var connection = Activator.CreateInstance(type, connectionString) as DbConnection;
        if (connection is null)
        {
            throw new InvalidOperationException("Could not create an NpgsqlConnection for the Lakona cluster directory.");
        }

        return connection;
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

internal sealed class LakonaClusterRpcSerializer
{
    public LakonaClusterRpcSerializer(IRpcSerializer serializer)
    {
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public IRpcSerializer Serializer { get; }
}
