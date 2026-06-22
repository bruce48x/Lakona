using System.Data.Common;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Server.App.Hosting;

internal static class AgarDatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddAgarDatabaseInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = CreateOptions(configuration);
        services.AddSingleton(options);
        services.AddSingleton<AgarDatabaseConnectionFactory>();
        services.AddSingleton(provider =>
        {
            var connections = provider.GetRequiredService<AgarDatabaseConnectionFactory>();
            return new SqlNodeDirectoryOptions(
                () => new ValueTask<DbConnection>(connections.CreatePostgresConnection()),
                SqlNodeDirectoryDialect.Postgres,
                options.NodeDirectoryTable);
        });
        services.AddSingleton<INodeDirectory, SqlNodeDirectory>();
        services.AddSingleton<IRouteDirectory, InMemoryRouteDirectory>();
        services.AddHostedService<AgarDatabaseSchemaHostedService>();
        return services;
    }

    private static AgarDatabaseOptions CreateOptions(IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString(AgarDatabaseOptions.PostgresConnectionName)
            ?? configuration["Agar:Database:Postgres"]
            ?? configuration["AGAR_POSTGRES_CONNECTION_STRING"];
        var redis = configuration.GetConnectionString(AgarDatabaseOptions.RedisConnectionName)
            ?? configuration["Agar:Database:Redis"]
            ?? configuration["AGAR_REDIS_CONNECTION_STRING"];
        var nodeDirectoryTable = configuration["Agar:Database:NodeDirectoryTable"]
            ?? AgarDatabaseOptions.DefaultNodeDirectoryTable;
        var ensureSchemaOnStartup = bool.TryParse(
            configuration["Agar:Database:EnsureSchemaOnStartup"],
            out var parsedEnsureSchemaOnStartup)
            && parsedEnsureSchemaOnStartup;

        if (string.IsNullOrWhiteSpace(postgres))
        {
            throw new InvalidOperationException(
                $"The database feature requires ConnectionStrings:{AgarDatabaseOptions.PostgresConnectionName}.");
        }

        if (string.IsNullOrWhiteSpace(redis))
        {
            throw new InvalidOperationException(
                $"The database feature requires ConnectionStrings:{AgarDatabaseOptions.RedisConnectionName}.");
        }

        return new AgarDatabaseOptions(postgres, redis, nodeDirectoryTable, ensureSchemaOnStartup);
    }
}
