using Dapper;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.App.Infrastructure.Redis;
using Server.Hotfix.Users;
using Server.Hotfix.Leaderboard;
using Xunit;

[assembly: AssemblyFixture(typeof(Agar.Unity.Tests.AgarPersistenceFixture))]

namespace Agar.Unity.Tests;

// These tests exercise the real stores. Supply dedicated PostgreSQL/Redis endpoints;
// each provider gets its own schema and Redis prefix, with cleanup owned by the fixture.
public sealed class AgarPersistenceFixture : IAsyncDisposable
{
    internal static readonly List<ServiceProvider> Providers = [];
    internal static readonly List<string> Schemas = [];
    internal static readonly List<string> Prefixes = [];
    internal static string Postgres => Environment.GetEnvironmentVariable("AGAR_TEST_POSTGRES")
        ?? "Host=127.0.0.1;Port=5432;Database=lakona-game;Username=lakona-game;Password=lakona-game_dev_password";
    internal static string Redis => Environment.GetEnvironmentVariable("AGAR_TEST_REDIS")
        ?? "127.0.0.1:6379,password=lakona-game_redis_dev_password";

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in Providers) await provider.DisposeAsync();
        if (Schemas.Count != 0)
        {
            await using var connection = new NpgsqlConnection(Postgres);
            await connection.OpenAsync();
            foreach (var schema in Schemas)
                await connection.ExecuteAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE;");
        }
        if (Prefixes.Count != 0)
        {
            using var connection = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Redis);
            foreach (var endpoint in connection.GetEndPoints())
            foreach (var prefix in Prefixes)
            {
                var server = connection.GetServer(endpoint);
                await foreach (var key in server.KeysAsync(pattern: prefix + ":*"))
                    await connection.GetDatabase().KeyDeleteAsync(key);
            }
        }
    }
}

internal static class TestPersistence
{
    public static void AddServices(IServiceCollection services)
    {
        var id = "test_" + Guid.NewGuid().ToString("N");
        var previous = services.LastOrDefault(item => item.ServiceType == typeof(IConfiguration))?.ImplementationInstance as IConfiguration;
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(previous ?? new ConfigurationBuilder().Build())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AgarGamePostgres"] = AgarPersistenceFixture.Postgres,
                ["ConnectionStrings:AgarGameRedis"] = AgarPersistenceFixture.Redis,
                ["Agar:Persistence:Redis:ConnectionStringName"] = "AgarGameRedis",
                ["Agar:Persistence:Redis:KeyPrefix"] = id
            }).Build();
        AgarPersistenceFixture.Prefixes.Add(id);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(_ =>
        {
            using var connection = new NpgsqlConnection(AgarPersistenceFixture.Postgres);
            connection.Open();
            connection.Execute($"CREATE SCHEMA {id};");
            AgarPersistenceFixture.Schemas.Add(id);
            return NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(AgarPersistenceFixture.Postgres)
            {
                SearchPath = id,
                Pooling = false
            }.ConnectionString);
        });
        ComponentStateRegistration.Register(services, [typeof(RedisLeaderboardState).Assembly]);
        // Direct Behavior tests manually activate actual components; DLL tests use generated registration and framework hooks.
        services.AddSingleton(provider =>
        {
            var store = new PostgresUserStore(provider.GetRequiredService<NpgsqlDataSource>());
            store.StartAsync(TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult();
            return store;
        });
        services.AddSingleton(provider =>
        {
            var store = new RedisLeaderboardStore(provider.GetRequiredService<RedisLeaderboardState>(), configuration);
            store.StartAsync(TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult();
            return store;
        });
    }

    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        AddServices(services);
        var provider = services.BuildServiceProvider();
        AgarPersistenceFixture.Providers.Add(provider);
        return provider;
    }
}
