using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Clustering.Redis;

/// <summary>Registers Redis-backed Lakona cluster membership.</summary>
public static class RedisClusteringServiceCollectionExtensions
{
    public const string ProviderName = "Redis";
    public const string DefaultKey = "lakona:{membership}:table";

    public static IServiceCollection AddLakonaRedisClustering(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration).Cluster.Membership;
        if (!string.Equals(options.Provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{options.ConnectionStringName} is required by the Redis membership provider.");
        }

        var key = configuration["Lakona:Cluster:Membership:Redis:Key"];
        return services.AddLakonaRedisClustering(
            connectionString,
            string.IsNullOrWhiteSpace(key) ? DefaultKey : key);
    }

    public static IServiceCollection AddLakonaRedisClustering(
        this IServiceCollection services,
        string connectionString,
        string key = DefaultKey)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!key.Contains('{') || !key.Contains('}'))
        {
            throw new ArgumentException(
                "The Redis Membership key must contain a Redis Cluster hash tag such as '{membership}'.",
                nameof(key));
        }

        services.Replace(ServiceDescriptor.Singleton<IMembershipTable>(_ =>
            new RedisMembershipTable(connectionString, key)));
        return services;
    }
}
