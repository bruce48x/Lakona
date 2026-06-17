using System.Data.Common;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Server.App.Features;

public sealed class DatabaseFeature : LakonaGameFeature
{
    public override bool Discoverable => false;

    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        var options = CreateOptions(context.Configuration);
        context.Services.AddSingleton(options);
        context.Services.AddSingleton<AgarDatabaseConnectionFactory>();
        context.Services.AddSingleton(provider =>
        {
            var connections = provider.GetRequiredService<AgarDatabaseConnectionFactory>();
            return new SqlNodeDirectoryOptions(
                () => new ValueTask<DbConnection>(connections.CreatePostgresConnection()),
                SqlNodeDirectoryDialect.Postgres,
                options.NodeDirectoryTable);
        });
        context.Services.AddSingleton<INodeDirectory, SqlNodeDirectory>();
        context.Services.AddSingleton<IRouteDirectory, InMemoryRouteDirectory>();
        context.Services.AddHostedService<AgarDatabaseSchemaHostedService>();
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
