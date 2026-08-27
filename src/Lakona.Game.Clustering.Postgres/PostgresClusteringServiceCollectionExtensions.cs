using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Lakona.Game.Clustering.Postgres;

/// <summary>Registers PostgreSQL-backed Lakona cluster membership.</summary>
public static class PostgresClusteringServiceCollectionExtensions
{
    public const string ProviderName = "Postgres";

    /// <summary>
    /// Selects PostgreSQL membership when <c>Lakona:Cluster:Membership:Provider</c>
    /// is <c>Postgres</c>.
    /// </summary>
    public static IServiceCollection AddLakonaPostgresClustering(
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
                $"ConnectionStrings:{options.ConnectionStringName} is required by the PostgreSQL membership provider.");
        }

        return services.AddLakonaPostgresClustering(connectionString);
    }

    /// <summary>Uses an explicit PostgreSQL connection string for membership.</summary>
    public static IServiceCollection AddLakonaPostgresClustering(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.Replace(ServiceDescriptor.Singleton<IMembershipTable>(
            _ => new PostgresMembershipTable(NpgsqlDataSource.Create(connectionString))));
        return services;
    }
}
