using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.App.Persistence;
using Server.App.State.Contracts;
using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Users;
using Server.App.State.Leaderboard;
using Server.App.State.Users;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarPersistenceTests
{
    [Fact]
    public async Task Stable_modules_succeed_without_creating_clients_when_connections_are_unconfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<Lakona.Game.Server.Health.LakonaServerReadinessState>();
        LakonaModuleDiscovery.ConfigureTypes(
            services,
            configuration,
            LakonaModuleDiscovery.DiscoverTypes(
                [typeof(AgarPostgresModule).Assembly]));
        await using var provider = services.BuildServiceProvider();

        await provider
            .GetRequiredService<LakonaModuleRuntime>()
            .StartAsync(TestContext.Current.CancellationToken);

        var userStore = provider.GetRequiredService<IUserStore>();
        var leaderboardStore = provider.GetRequiredService<ILeaderboardStore>();

        Assert.Null(provider.GetService<NpgsqlDataSource>());
        Assert.Null(provider.GetService<RedisLeaderboardOptions>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            userStore.LoadAsync(
                "misrouted-user",
                TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            leaderboardStore.GetCurrentPeriodAsync(
                TestContext.Current.CancellationToken).AsTask());

        await provider
            .GetRequiredService<LakonaModuleRuntime>()
            .StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stable_modules_register_postgres_and_redis_adapters_in_the_root_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AgarGamePostgres"] =
                    "Host=127.0.0.1;Database=agar;Username=agar;Password=test",
                ["ConnectionStrings:AgarGameRedis"] =
                    "127.0.0.1:6379,password=test"
            })
            .Build();
        var services = new ServiceCollection();
        var discovered = LakonaModuleDiscovery.DiscoverTypes(
            [typeof(AgarPostgresModule).Assembly]);
        var catalog = LakonaModuleDiscovery.ConfigureTypes(
            services,
            configuration,
            discovered);
        await using var provider = services.BuildServiceProvider();

        var userStore = provider.GetRequiredService<IUserStore>();
        var leaderboardStore = provider.GetRequiredService<ILeaderboardStore>();
        var modules = provider.GetServices<ILakonaModule>().ToArray();

        Assert.IsType<PostgresUserStore>(userStore);
        Assert.IsType<RedisLeaderboardStore>(leaderboardStore);
        Assert.Collection(
            catalog.Modules,
            module => Assert.IsType<AgarPostgresModule>(module.Instance),
            module => Assert.IsType<AgarRedisModule>(module.Instance));
        Assert.Contains(modules, static module => module is AgarPostgresModule);
        Assert.Contains(modules, static module => module is AgarRedisModule);
    }

    [Fact]
    public async Task User_profile_survives_actor_recreation()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        await using var provider = services.BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var actorId = ActorId.From("persisted-user");
        var cancellationToken = TestContext.Current.CancellationToken;

        await hosting.EnsureAsync<UserActor>(actorId, cancellationToken);
        var first = await actors.AskAsync<UserActor, UserLoginResult>(
            actorId,
            (actor, _) => actor.LoginAndAttachAsync(
                new UserLoginAndAttachRequest
                {
                    Password = "pw",
                    ConnectionId = "first",
                    ControlSessionId = "first-session"
                }),
            cancellationToken);
        await actors.TellAsync<UserActor>(
            actorId,
            (actor, _) => actor.AddVictoryPointsAsync(
                new UserVictoryPointsRequest { Points = 25 }),
            cancellationToken);

        await hosting.DestroyAsync<UserActor>(actorId, cancellationToken);
        await hosting.EnsureAsync<UserActor>(actorId, cancellationToken);
        var second = await actors.AskAsync<UserActor, UserLoginResult>(
            actorId,
            (actor, _) => actor.LoginAndAttachAsync(
                new UserLoginAndAttachRequest
                {
                    Password = "pw",
                    ConnectionId = "second",
                    ControlSessionId = "second-session"
                }),
            cancellationToken);

        Assert.Equal(1, first.LoginCount);
        Assert.Equal(2, second.LoginCount);
        Assert.Equal(25, second.VictoryPoints);
    }

    [Fact]
    public async Task Leaderboard_survives_actor_recreation()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        await using var provider = services.BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var actorId = ActorId.From("persisted-leaderboard");
        var cancellationToken = TestContext.Current.CancellationToken;

        await hosting.EnsureAsync<LeaderboardActor>(actorId, cancellationToken);
        await actors.TellAsync<LeaderboardActor>(
            actorId,
            (actor, _) => actor.RecordVictoryPointsAsync(
                new LeaderboardVictoryPointsRequest
                {
                    PlayerId = "player-a",
                    VictoryPoints = 40,
                    WinCount = 3
                }),
            cancellationToken);

        await hosting.DestroyAsync<LeaderboardActor>(actorId, cancellationToken);
        await hosting.EnsureAsync<LeaderboardActor>(actorId, cancellationToken);
        var snapshot = await actors.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            actorId,
            (actor, _) => actor.GetLeaderboardAsync(
                new LeaderboardQueryRequest { TopN = 10 }),
            cancellationToken);

        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal("player-a", entry.PlayerId);
        Assert.Equal(40, entry.VictoryPoints);
        Assert.Equal(3, entry.WinCount);
    }
}
