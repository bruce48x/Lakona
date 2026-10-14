using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Server.Hotfix.Users;
using Server.Hotfix.Leaderboard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Server.App.Routing;
using Server.App.Infrastructure.Redis;
using Server.App.Leaderboard;
using Server.App.Users;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarPersistenceTests
{
    [Theory]
    [InlineData("Lakona:Node:Roles", "[\"data\"]")]
    [InlineData("Lakona:Node:Roles:0", "data")]
    public async Task Data_registration_uses_framework_role_parsing(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = value,
            ["ConnectionStrings:AgarGamePostgres"] = "Host=localhost;Database=test;Username=test"
        }).Build();
        var services = new ServiceCollection();
        using var registration = HotfixComponentRegistration.Configure(services, typeof(PostgresUserStore).Assembly, configuration,
            Lakona.Game.Server.Configuration.LakonaGameNodeOptions.FromConfiguration(configuration.GetSection("Lakona:Node")).Roles);
        await using var provider = services.BuildServiceProvider();
        registration.Initialize(provider);
        Assert.NotNull(provider.GetService<NpgsqlDataSource>());
    }

    [Fact]
    public async Task Gateway_registration_does_not_require_data_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lakona:Node:Roles:0"] = "gateway"
        }).Build();
        var services = new ServiceCollection();
        using var registration = HotfixComponentRegistration.Configure(services, typeof(PostgresUserStore).Assembly, configuration, ["gateway"]);
        await using var provider = services.BuildServiceProvider();
        registration.Initialize(provider);
        Assert.Null(provider.GetService<NpgsqlDataSource>());
        Assert.Null(provider.GetService<RedisLeaderboardState>());
    }

    [Fact]
    public async Task Hotfix_stores_reuse_stable_resources_across_reload()
    {
        await using var root = TestHotfix.CreateRootServiceProvider();
        var path = TestHotfix.FindHotfixAssemblyPath();
        await using var manager = new HotfixManager(
            new Lakona.Game.Server.Hotfix.Loading.CurrentDirectoryHotfixAssemblySource(Path.GetDirectoryName(path)!, Path.GetFileName(path)),
            TestHotfix.HostAssemblyNames(), rootServices: root);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, TestHotfix.BuildReloadDiagnostics(first));
        var original = ((IHotfixRuntimeAccessor)manager).Current;
        var state = root.GetRequiredService<RedisLeaderboardState>();
        var connection = state.Connection;
        var dataSource = root.GetRequiredService<NpgsqlDataSource>();
        await connection!.GetDatabase().StringSetAsync(state.KeyPrefix + ":reload-probe", "retained");
        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded, TestHotfix.BuildReloadDiagnostics(second));
        var latest = ((IHotfixRuntimeAccessor)manager).Current;
        Assert.NotSame(original, latest);
        Assert.Same(state, latest.Services.GetRequiredService<RedisLeaderboardState>());
        Assert.Same(connection, state.Connection);
        Assert.Same(dataSource, latest.Services.GetRequiredService<NpgsqlDataSource>());
        Assert.Equal("retained", (string?)await connection.GetDatabase().StringGetAsync(state.KeyPrefix + ":reload-probe"));
    }

    [Fact]
    public async Task User_login_count_survives_actor_recreation()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        await using var provider = services.BuildReadyServiceProvider(
            TestContext.Current.CancellationToken);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
    }

    [Fact]
    public async Task Leaderboard_survives_actor_recreation()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        await using var provider = services.BuildReadyServiceProvider(
            TestContext.Current.CancellationToken);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
