using Lakona.Game.Server.Modules;
using Lakona.Game.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Leaderboard;
using StackExchange.Redis;

namespace Server.App.Infrastructure.Redis;

[NodeRole("data")]
public sealed class AgarRedisModule : ILakonaModule
{
    private ConnectionMultiplexer? connection;
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = CreateOptions(configuration);
        services.AddSingleton(options);
        services.AddSingleton<ConnectionMultiplexer>(_ =>
            Volatile.Read(ref connection)
            ?? throw new InvalidOperationException(
                "The Agar Redis module has not completed startup."));
        services.AddSingleton<IDatabase>(provider =>
            provider.GetRequiredService<ConnectionMultiplexer>().GetDatabase());
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

            var registered = context.Services
                .GetRequiredService<ConnectionMultiplexer>();
            if (!ReferenceEquals(candidate, registered))
            {
                throw new InvalidOperationException(
                    "The Redis DI singleton does not match the connected instance.");
            }
        }
        catch
        {
            if (candidate is not null)
            {
                _ = Interlocked.CompareExchange(
                    ref connection,
                    null,
                    candidate);
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
                $"ConnectionStrings:{connectionStringName} is required by the Agar Redis module on nodes with role 'data'.");
        }

        var keyPrefix = configuration["Agar:Persistence:Redis:KeyPrefix"];
        return new RedisLeaderboardOptions(
            connectionString,
            string.IsNullOrWhiteSpace(keyPrefix) ? "agar" : keyPrefix.Trim());
    }
}
