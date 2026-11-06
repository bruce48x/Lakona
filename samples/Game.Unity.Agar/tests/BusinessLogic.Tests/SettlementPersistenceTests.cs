using Dapper;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.App.Routing;
using Server.App.Users;
using Server.Hotfix.Users;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class SettlementPersistenceTests
{
    [Fact]
    public async Task Replaying_a_committed_win_after_actor_recreation_counts_it_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = TestHotfix.CreateRootServiceProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var store = provider.GetRequiredService<PostgresUserStore>();
        var id = ActorIdentity.Create<UserActor, UserId>(new UserId("winner"));
        var request = new UserWinRequest { SettlementId = "match-1:win" };
        await TestPersistence.InsertUserAsync(provider, ExistingUser("winner"), ct);
        await catalog.EnsureAsync<UserActor>(id, ct);

        await actors.TellAsync<UserActor>(id, (actor, _) => actor.AddWinAsync(request), ct);
        await actors.TellAsync<UserActor>(id, (actor, _) => actor.AddWinAsync(request), ct);
        Assert.Equal(4, (await store.LoadAsync("winner", ct))!.WinCount);

        await catalog.DestroyAsync<UserActor>(id, ct);
        await catalog.EnsureAsync<UserActor>(id, ct);
        await actors.TellAsync<UserActor>(id, (actor, _) => actor.AddWinAsync(request), ct);
        Assert.Equal(4, (await store.LoadAsync("winner", ct))!.WinCount);
    }

    [Fact]
    public async Task Concurrent_retries_count_once_per_player_and_operation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = TestPersistence.CreateProvider();
        var store = provider.GetRequiredService<PostgresUserStore>();
        await TestPersistence.InsertUserAsync(provider, ExistingUser("alice"), ct);
        await TestPersistence.InsertUserAsync(provider, ExistingUser("bob"), ct);

        var retries = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct).AsTask()));
        Assert.Single(retries, result => result is { Applied: true });
        Assert.All(retries, result => Assert.Equal(25, result?.VictoryPoints));

        await Task.WhenAll(
            store.ApplySettlementAsync("alice", "match-1:win", 1, 0, ct).AsTask(),
            store.ApplySettlementAsync("alice", "match-2:points", 0, 7, ct).AsTask(),
            store.ApplySettlementAsync("alice", "match-3:points", 0, 11, ct).AsTask(),
            store.ApplySettlementAsync("bob", "match-1:points", 0, 5, ct).AsTask());
        var replay = await store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct);
        Assert.Equal((4, 43, false), replay);
        var alice = await store.LoadAsync("alice", ct);
        var bob = await store.LoadAsync("bob", ct);
        Assert.Equal((4, 43), (alice!.WinCount, alice.VictoryPoints));
        Assert.Equal((3, 25), (bob!.WinCount, bob.VictoryPoints));
    }

    [Fact]
    public async Task Failed_award_leaves_the_same_settlement_available_for_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = TestPersistence.CreateProvider();
        var store = provider.GetRequiredService<PostgresUserStore>();
        await TestPersistence.InsertUserAsync(provider, ExistingUser("alice"), ct);
        await using var connection = await provider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await connection.ExecuteAsync("ALTER TABLE agar_users ADD CONSTRAINT reject_award CHECK (victory_points <= 20)");

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct).AsTask());
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(20, (await store.LoadAsync("alice", ct))!.VictoryPoints);

        await connection.ExecuteAsync("ALTER TABLE agar_users DROP CONSTRAINT reject_award");
        var retry = await store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct);
        Assert.Equal((3, 25, true), retry);
        await store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct);
        Assert.Equal(25, (await store.LoadAsync("alice", ct))!.VictoryPoints);
    }

    [Fact]
    public async Task Login_with_cached_profile_preserves_newly_committed_awards()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = TestHotfix.CreateRootServiceProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var store = provider.GetRequiredService<PostgresUserStore>();
        var id = ActorIdentity.Create<UserActor, UserId>(new UserId("alice"));
        var login = new UserLoginAndAttachRequest { Password = "pw", ConnectionId = "control", ControlSessionId = "session" };
        await catalog.EnsureAsync<UserActor>(id, ct);
        await actors.AskAsync<UserActor, UserLoginResult>(id, (actor, _) => actor.LoginAndAttachAsync(login), ct);

        await store.ApplySettlementAsync("alice", "match-1:win", 1, 0, ct);
        await store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct);
        var second = await actors.AskAsync<UserActor, UserLoginResult>(id, (actor, _) => actor.LoginAndAttachAsync(login), ct);
        Assert.Equal((1, 5), (second.WinCount, second.VictoryPoints));
        var persisted = await store.LoadAsync("alice", ct);
        Assert.Equal((1, 5), (persisted!.WinCount, persisted.VictoryPoints));
    }

    [Fact]
    public async Task Resetting_points_preserves_wins_and_old_settlement_receipts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = TestPersistence.CreateProvider();
        var store = provider.GetRequiredService<PostgresUserStore>();
        await TestPersistence.InsertUserAsync(provider, ExistingUser("alice"), ct);
        await store.ApplySettlementAsync("alice", "match-1:win", 1, 0, ct);
        await store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct);

        await store.ResetVictoryPointsAsync("alice", ct);
        var replay = await store.ApplySettlementAsync("alice", "match-1:points", 0, 5, ct);
        Assert.Equal((4, 0, false), replay);
        var next = await store.ApplySettlementAsync("alice", "match-2:points", 0, 7, ct);
        Assert.Equal((4, 7, true), next);
    }

    [Fact]
    public async Task An_award_requires_an_existing_player_and_a_nonblank_operation_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = TestPersistence.CreateProvider();
        var store = provider.GetRequiredService<PostgresUserStore>();
        Assert.Null(await store.ApplySettlementAsync("alice", "match-1:win", 1, 0, ct));

        await TestPersistence.InsertUserAsync(provider, ExistingUser("alice"), ct);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplySettlementAsync("alice", " ", 1, 0, ct).AsTask());
        Assert.Equal((4, 20, true), await store.ApplySettlementAsync("alice", "match-1:win", 1, 0, ct));
    }

    private static PersistedUser ExistingUser(string id) => new()
    {
        UserId = id,
        PasswordHash = "hash",
        CreatedAtUtc = DateTime.UtcNow,
        LastLoginAtUtc = DateTime.UtcNow,
        WinCount = 3,
        VictoryPoints = 20
    };
}
