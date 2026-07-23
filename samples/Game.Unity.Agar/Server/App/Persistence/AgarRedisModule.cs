using Lakona.Game.Server.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Server.App.Persistence;

public sealed class AgarRedisModule : ILakonaModule
{
    private ConnectionMultiplexer? connection;

    internal IDatabase Database =>
        Volatile.Read(ref connection)?.GetDatabase()
        ?? throw new InvalidOperationException(
            "The Agar Redis module has not completed startup.");

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(CreateOptions(configuration));
        services.AddSingleton<RedisLeaderboardStore>();
        services.AddSingleton<ILeaderboardStore>(provider =>
            provider.GetRequiredService<RedisLeaderboardStore>());
    }

    public async Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken)
    {
        var options = context.Services.GetRequiredService<RedisLeaderboardOptions>();
        var configuration = ConfigurationOptions.Parse(options.ConnectionString);
        configuration.AbortOnConnectFail = true;

        ConnectionMultiplexer? candidate = null;
        try
        {
            candidate = await ConnectionMultiplexer
                .ConnectAsync(configuration)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = await candidate
                .GetDatabase()
                .PingAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref connection, candidate);
        }
        catch
        {
            if (candidate is not null)
            {
                await candidate.CloseAsync(
                    allowCommandsToComplete: false).ConfigureAwait(false);
                candidate.Dispose();
            }

            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var current = Interlocked.Exchange(ref connection, null);
        if (current is null)
        {
            return;
        }

        await current.CloseAsync(
            allowCommandsToComplete: true).ConfigureAwait(false);
        current.Dispose();
    }

    private static RedisLeaderboardOptions CreateOptions(
        IConfiguration configuration)
    {
        var connectionStringName =
            configuration["Agar:Persistence:Redis:ConnectionStringName"]
            ?? "AgarGameRedis";
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringName} must contain the Agar Redis connection string.");
        }

        var keyPrefix = configuration["Agar:Persistence:Redis:KeyPrefix"];
        return new RedisLeaderboardOptions(
            connectionString,
            string.IsNullOrWhiteSpace(keyPrefix) ? "agar" : keyPrefix.Trim());
    }
}
