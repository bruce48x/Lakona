using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public abstract class MembershipTableContractTests
{
    private protected abstract ValueTask<IMembershipTable?> CreateTableAsync();

    [Fact]
    public Task GenerationsAreMonotonicWithinOneClusterIncarnation() => RunAsync(async (table, clusterId, ct) =>
    {
        var first = await table.AllocateGenerationAsync(clusterId, ct);
        var second = await table.AllocateGenerationAsync(clusterId, ct);

        Assert.Equal(first.Cluster, second.Cluster);
        Assert.Equal(first.Value + 1, second.Value);
    });

    [Fact]
    public Task ConcurrentWritersCannotCommitTheSameTableVersion() => RunAsync(async (table, clusterId, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(clusterId, ct);
        var first = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var second = Joining(initial.Cluster, "server-2", "22222222-2222-2222-2222-222222222222");

        var writes = await Task.WhenAll(
            table.TryInsertAsync(clusterId, first, initial.Version, ct).AsTask(),
            table.TryInsertAsync(clusterId, second, initial.Version, ct).AsTask());

        Assert.Single(writes, static committed => committed);
        var committed = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.Equal(new MembershipViewId(1), committed.Version);
        Assert.Single(committed.Entries);
    });

    [Fact]
    public Task ConcurrentUpdatesEventuallyCommitOneWriterPerTableVersion() => RunAsync(async (table, clusterId, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(clusterId, ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(clusterId, joining, initial.Version, ct));

        const int writerCount = 20;
        await Task.WhenAll(Enumerable.Range(0, writerCount).Select(async _ =>
        {
            while (true)
            {
                var snapshot = await table.ReadOrCreateAsync(clusterId, ct);
                var current = Assert.Single(snapshot.Entries);
                if (await table.TryUpdateAsync(
                    clusterId,
                    current.WithStatus(MembershipTableStatus.Joining),
                    current.Version,
                    snapshot.Version,
                    ct))
                    return;
            }
        }));

        var committed = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.Equal(writerCount + 1, committed.Version.Value);
        Assert.Equal(writerCount + 1, Assert.Single(committed.Entries).Version);
    });

    [Fact]
    public Task AStableNodeCannotRejoinUntilItsPreviousIncarnationIsDead() => RunAsync(async (table, clusterId, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(clusterId, ct);
        var first = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var restarted = Joining(initial.Cluster, "server-1", "22222222-2222-2222-2222-222222222222");

        Assert.True(await table.TryInsertAsync(clusterId, first, initial.Version, ct));
        var afterInsert = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.False(await table.TryInsertAsync(clusterId, restarted, afterInsert.Version, ct));
        Assert.True(await table.TryUpdateAsync(
            clusterId,
            first.WithStatus(MembershipTableStatus.Dead),
            first.Version,
            afterInsert.Version,
            ct));
        var afterDeath = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.True(await table.TryInsertAsync(clusterId, restarted, afterDeath.Version, ct));
    });

    [Fact]
    public Task ReplacementFencesOldIncarnationAndInsertsNewIncarnationInOneTableVersion() => RunAsync(async (table, clusterId, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(clusterId, ct);
        var first = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var replacement = Joining(
            initial.Cluster,
            "server-1",
            "22222222-2222-2222-2222-222222222222",
            generation: 2);
        Assert.True(await table.TryInsertAsync(clusterId, first, initial.Version, ct));
        var beforeReplacement = await table.ReadOrCreateAsync(clusterId, ct);

        Assert.True(await table.TryReplaceAsync(
            clusterId,
            first.Reference,
            first.Version,
            replacement,
            beforeReplacement.Version,
            ct));

        var committed = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.Equal(beforeReplacement.Version.Value + 1, committed.Version.Value);
        Assert.Equal(
            MembershipTableStatus.Dead,
            committed.Entries.Single(entry => entry.Reference == first.Reference).Status);
        Assert.Equal(
            MembershipTableStatus.Joining,
            committed.Entries.Single(entry => entry.Reference == replacement.Reference).Status);
    });

    [Fact]
    public Task DeadIsFinalAndRejectsHeartbeats() => RunAsync(async (table, clusterId, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(clusterId, ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(clusterId, joining, initial.Version, ct));
        var inserted = await table.ReadOrCreateAsync(clusterId, ct);
        var dead = joining.WithStatus(MembershipTableStatus.Dead);
        Assert.True(await table.TryUpdateAsync(clusterId, dead, joining.Version, inserted.Version, ct));
        var afterDeath = await table.ReadOrCreateAsync(clusterId, ct);

        Assert.False(await table.TryUpdateAsync(
            clusterId,
            dead.WithStatus(MembershipTableStatus.Active),
            dead.Version,
            afterDeath.Version,
            ct));
        Assert.False(await table.TryUpdateIAmAliveAsync(
            clusterId,
            dead.Reference,
            dead.IAmAliveTime.AddSeconds(1),
            ct));
        Assert.Equal(afterDeath.Version, (await table.ReadOrCreateAsync(clusterId, ct)).Version);
    });

    [Fact]
    public Task HeartbeatRefreshesLivenessWithoutCreatingAClusterWideChange() => RunAsync(async (table, clusterId, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(clusterId, ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(clusterId, joining, initial.Version, ct));
        var inserted = await table.ReadOrCreateAsync(clusterId, ct);
        var refreshedAt = joining.IAmAliveTime.AddSeconds(1);

        Assert.True(await table.TryUpdateIAmAliveAsync(clusterId, joining.Reference, refreshedAt, ct));

        var refreshed = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.Equal(inserted.Version, refreshed.Version);
        Assert.Equal(refreshedAt, Assert.Single(refreshed.Entries).IAmAliveTime);
        Assert.Equal(joining.Version, Assert.Single(refreshed.Entries).Version);
    });

    [Fact]
    public Task DefunctCleanupIsBoundedAndDoesNotCreateAMembershipView() => RunAsync(async (table, clusterId, ct) =>
    {
        var snapshot = await table.ReadOrCreateAsync(clusterId, ct);
        foreach (var entry in new[]
                 {
                     Joining(snapshot.Cluster, "server-1", "11111111-1111-1111-1111-111111111111"),
                     Joining(snapshot.Cluster, "server-2", "22222222-2222-2222-2222-222222222222")
                 })
        {
            Assert.True(await table.TryInsertAsync(clusterId, entry, snapshot.Version, ct));
            snapshot = await table.ReadOrCreateAsync(clusterId, ct);
            Assert.True(await table.TryUpdateAsync(
                clusterId,
                entry.WithStatus(MembershipTableStatus.Dead),
                entry.Version,
                snapshot.Version,
                ct));
            snapshot = await table.ReadOrCreateAsync(clusterId, ct);
        }

        var viewBeforeCleanup = snapshot.Version;
        Assert.Equal(1, await table.CleanupDefunctAsync(
            clusterId,
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            maximumRows: 1,
            ct));

        var afterFirstPass = await table.ReadOrCreateAsync(clusterId, ct);
        Assert.Equal(viewBeforeCleanup, afterFirstPass.Version);
        Assert.Single(afterFirstPass.Entries);
        Assert.Equal(1, await table.CleanupDefunctAsync(
            clusterId,
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            maximumRows: 1,
            ct));
        Assert.Empty((await table.ReadOrCreateAsync(clusterId, ct)).Entries);
    });

    private async Task RunAsync(Func<IMembershipTable, string, CancellationToken, Task> test)
    {
        var table = await CreateTableAsync();
        if (table is null) return;
        try
        {
            await test(
                table,
                $"contract-{Guid.NewGuid():N}",
                TestContext.Current.CancellationToken);
        }
        finally
        {
            if (table is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    private static MembershipTableEntry Joining(
        ClusterIncarnationId cluster,
        string node,
        string incarnation,
        long generation = 1) =>
        new(
            new NodeReference(cluster, new NodeId(node), new NodeIncarnationId(Guid.Parse(incarnation))),
            MembershipTableStatus.Joining,
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            version: 1,
            iAmAliveTime: DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            generation: generation);
}
