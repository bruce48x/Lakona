using Dapper;
using Lakona.Game.Server.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Server.App.Persistence;

public sealed class AgarPostgresModule : ILakonaModule
{
    private bool enabled;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = ResolveConnectionString(configuration);
        if (connectionString is null)
        {
            services.AddSingleton<IUserStore, UnconfiguredUserStore>();
            return;
        }

        enabled = true;
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresUserStore>();
        services.AddSingleton<IUserStore>(provider =>
            provider.GetRequiredService<PostgresUserStore>());
    }

    public async Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return;
        }

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

    private static string? ResolveConnectionString(
        IConfiguration configuration)
    {
        var connectionStringName =
            configuration["Agar:Persistence:Postgres:ConnectionStringName"]
            ?? "AgarGamePostgres";
        var connectionString = configuration.GetConnectionString(connectionStringName);
        return string.IsNullOrWhiteSpace(connectionString)
            ? null
            : connectionString;
    }
}
