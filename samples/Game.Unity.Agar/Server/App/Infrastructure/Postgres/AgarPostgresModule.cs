using Dapper;
using Lakona.Game.Server.Modules;
using Lakona.Game.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.App.Users;

namespace Server.App.Infrastructure.Postgres;

[NodeRole("data")]
public sealed class AgarPostgresModule : ILakonaModule
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = ResolveConnectionString(configuration);
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresUserStore>();
        services.AddSingleton<IUserStore>(provider =>
            provider.GetRequiredService<PostgresUserStore>());
    }

    public async Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken)
    {
        var store = context.Services.GetRequiredService<PostgresUserStore>();
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var dataSource = context.Services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        _ = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT 1;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static string ResolveConnectionString(
        IConfiguration configuration)
    {
        var connectionStringName =
            configuration["Agar:Persistence:Postgres:ConnectionStringName"]
            ?? "AgarGamePostgres";
        var connectionString = configuration.GetConnectionString(connectionStringName);
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringName} is required by the Agar PostgreSQL module on nodes with role 'data'.")
            : connectionString;
    }
}
