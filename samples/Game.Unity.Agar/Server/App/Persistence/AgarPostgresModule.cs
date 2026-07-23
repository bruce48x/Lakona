using Dapper;
using Lakona.Game.Server.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Server.App.Persistence;

public sealed class AgarPostgresModule : ILakonaModule
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(_ => CreateDataSource(configuration));
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

    private static NpgsqlDataSource CreateDataSource(
        IConfiguration configuration)
    {
        var connectionStringName =
            configuration["Agar:Persistence:Postgres:ConnectionStringName"]
            ?? "AgarGamePostgres";
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringName} must contain the Agar PostgreSQL connection string.");
        }

        return NpgsqlDataSource.Create(connectionString);
    }
}
