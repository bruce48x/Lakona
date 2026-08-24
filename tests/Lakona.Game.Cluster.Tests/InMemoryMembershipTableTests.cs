using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class InMemoryMembershipTableTests
{
    [Fact]
    public async Task GenerationsAreMonotonicWithinOneClusterIncarnation()
    {
        var table = new InMemoryMembershipTable();
        var first = await table.AllocateGenerationAsync("game", TestContext.Current.CancellationToken);
        var second = await table.AllocateGenerationAsync("game", TestContext.Current.CancellationToken);

        Assert.Equal(first.Cluster, second.Cluster);
        Assert.Equal(first.Value + 1, second.Value);
    }

    [Fact]
    public async Task ConcurrentWritersCannotCommitTheSameTableVersion()
    {
        var table = new InMemoryMembershipTable();
        var initial = await table.ReadOrCreateAsync("game", TestContext.Current.CancellationToken);
        var first = CreateJoining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var second = CreateJoining(initial.Cluster, "server-2", "22222222-2222-2222-2222-222222222222");

        var writes = await Task.WhenAll(
            table.TryInsertAsync("game", first, initial.Version, TestContext.Current.CancellationToken).AsTask(),
            table.TryInsertAsync("game", second, initial.Version, TestContext.Current.CancellationToken).AsTask());

        Assert.Single(writes, static committed => committed);
        var committed = await table.ReadOrCreateAsync("game", TestContext.Current.CancellationToken);
        Assert.Equal(new MembershipViewId(1), committed.Version);
        Assert.Single(committed.Entries);
    }

    [Fact]
    public async Task AStableNodeCannotRejoinUntilItsPreviousIncarnationIsDead()
    {
        var table = new InMemoryMembershipTable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var initial = await table.ReadOrCreateAsync("game", cancellationToken);
        var first = CreateJoining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var restarted = CreateJoining(initial.Cluster, "server-1", "22222222-2222-2222-2222-222222222222");

        Assert.True(await table.TryInsertAsync("game", first, initial.Version, cancellationToken));
        var afterInsert = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.False(await table.TryInsertAsync("game", restarted, afterInsert.Version, cancellationToken));
        Assert.True(await table.TryUpdateAsync("game", first.WithStatus(MembershipTableStatus.Dead), first.Version, afterInsert.Version, cancellationToken));
        var afterDeath = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.True(await table.TryInsertAsync("game", restarted, afterDeath.Version, cancellationToken));
    }

    [Fact]
    public async Task ReplacementFencesOldIncarnationAndInsertsNewIncarnationInOneTableVersion()
    {
        var table = new InMemoryMembershipTable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var initial = await table.ReadOrCreateAsync("game", cancellationToken);
        var first = CreateJoining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var replacement = CreateJoining(initial.Cluster, "server-1", "22222222-2222-2222-2222-222222222222", generation: 2);
        Assert.True(await table.TryInsertAsync("game", first, initial.Version, cancellationToken));
        var beforeReplacement = await table.ReadOrCreateAsync("game", cancellationToken);

        Assert.True(await table.TryReplaceAsync(
            "game",
            first.Reference,
            first.Version,
            replacement,
            beforeReplacement.Version,
            cancellationToken));

        var committed = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.Equal(beforeReplacement.Version.Value + 1, committed.Version.Value);
        Assert.Equal(MembershipTableStatus.Dead, committed.Entries.Single(entry => entry.Reference == first.Reference).Status);
        Assert.Equal(MembershipTableStatus.Joining, committed.Entries.Single(entry => entry.Reference == replacement.Reference).Status);
    }

    [Fact]
    public async Task DeadIsFinalAndRejectsHeartbeats()
    {
        var table = new InMemoryMembershipTable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var initial = await table.ReadOrCreateAsync("game", cancellationToken);
        var joining = CreateJoining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync("game", joining, initial.Version, cancellationToken));
        var inserted = await table.ReadOrCreateAsync("game", cancellationToken);
        var dead = joining.WithStatus(MembershipTableStatus.Dead);
        Assert.True(await table.TryUpdateAsync("game", dead, joining.Version, inserted.Version, cancellationToken));
        var afterDeath = await table.ReadOrCreateAsync("game", cancellationToken);

        Assert.False(await table.TryUpdateAsync("game", dead.WithStatus(MembershipTableStatus.Active), dead.Version, afterDeath.Version, cancellationToken));
        Assert.False(await table.TryUpdateIAmAliveAsync("game", dead.Reference, dead.IAmAliveTime.AddSeconds(1), cancellationToken));
        Assert.Equal(afterDeath.Version, (await table.ReadOrCreateAsync("game", cancellationToken)).Version);
    }

    [Fact]
    public async Task HeartbeatRefreshesLivenessWithoutCreatingAClusterWideChange()
    {
        var table = new InMemoryMembershipTable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var initial = await table.ReadOrCreateAsync("game", cancellationToken);
        var joining = CreateJoining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync("game", joining, initial.Version, cancellationToken));
        var inserted = await table.ReadOrCreateAsync("game", cancellationToken);
        var refreshedAt = joining.IAmAliveTime.AddSeconds(1);

        Assert.True(await table.TryUpdateIAmAliveAsync("game", joining.Reference, refreshedAt, cancellationToken));

        var refreshed = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.Equal(inserted.Version, refreshed.Version);
        Assert.Equal(refreshedAt, Assert.Single(refreshed.Entries).IAmAliveTime);
        Assert.Equal(joining.Version, Assert.Single(refreshed.Entries).Version);
    }

    [Fact]
    public async Task DefunctCleanupIsBoundedAndDoesNotCreateAMembershipView()
    {
        var table = new InMemoryMembershipTable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshot = await table.ReadOrCreateAsync("game", cancellationToken);
        foreach (var entry in new[]
                 {
                     CreateJoining(snapshot.Cluster, "server-1", "11111111-1111-1111-1111-111111111111"),
                     CreateJoining(snapshot.Cluster, "server-2", "22222222-2222-2222-2222-222222222222")
                 })
        {
            Assert.True(await table.TryInsertAsync("game", entry, snapshot.Version, cancellationToken));
            snapshot = await table.ReadOrCreateAsync("game", cancellationToken);
            Assert.True(await table.TryUpdateAsync(
                "game",
                entry.WithStatus(MembershipTableStatus.Dead),
                entry.Version,
                snapshot.Version,
                cancellationToken));
            snapshot = await table.ReadOrCreateAsync("game", cancellationToken);
        }

        var viewBeforeCleanup = snapshot.Version;
        Assert.Equal(1, await table.CleanupDefunctAsync(
            "game",
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            maximumRows: 1,
            cancellationToken));

        var afterFirstPass = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.Equal(viewBeforeCleanup, afterFirstPass.Version);
        Assert.Single(afterFirstPass.Entries);
        Assert.Equal(1, await table.CleanupDefunctAsync(
            "game",
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            maximumRows: 1,
            cancellationToken));
        Assert.Empty((await table.ReadOrCreateAsync("game", cancellationToken)).Entries);
    }

    private static MembershipTableEntry CreateJoining(
        ClusterIncarnationId cluster,
        string nodeId,
        string incarnation,
        long generation = 1) =>
        new(
            new NodeReference(cluster, new NodeId(nodeId), new NodeIncarnationId(Guid.Parse(incarnation))),
            MembershipTableStatus.Joining,
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            version: 1,
            iAmAliveTime: DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            generation: generation);
}
