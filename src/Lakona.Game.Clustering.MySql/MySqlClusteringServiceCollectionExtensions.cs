using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

namespace Lakona.Game.Clustering.MySql;

/// <summary>Registers MySQL-backed Lakona cluster membership.</summary>
public static class MySqlClusteringServiceCollectionExtensions
{
    public const string ProviderName = "MySql";

    public static IServiceCollection AddLakonaMySqlClustering(
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
                $"ConnectionStrings:{options.ConnectionStringName} is required by the MySQL membership provider.");
        }

        return services.AddLakonaMySqlClustering(connectionString);
    }

    public static IServiceCollection AddLakonaMySqlClustering(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.Replace(ServiceDescriptor.Singleton<IMembershipTable>(_
            => new MySqlMembershipTable(new MySqlDataSource(connectionString))));
        return services;
    }
}
