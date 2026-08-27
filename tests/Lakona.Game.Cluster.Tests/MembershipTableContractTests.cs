using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public abstract class MembershipTableContractTests
{
    private const string BuildTag = "Release1";

    private protected abstract ValueTask<IMembershipTable?> CreateTableAsync();

    [Fact]
    public Task NewTableStartsEmptyAtVersionZeroWithoutABuildTag() => RunAsync(async (table, ct) =>
    {
        var snapshot = await table.ReadOrCreateAsync(ct);

        Assert.Empty(snapshot.Entries);
        Assert.Equal(new MembershipViewId(0), snapshot.Version);
        Assert.Null(snapshot.BuildTag);
    });

    [Fact]
    public Task CompleteEntrySurvivesInsertAndUpdate() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var joining = RichJoining(initial.Cluster);

        Assert.True(await table.TryInsertAsync(joining, initial.Version, ct));
        var inserted = await table.ReadOrCreateAsync(ct);
        AssertEntryEqual(joining, Assert.Single(inserted.Entries));

        var active = joining.WithStatus(MembershipTableStatus.Active);
        Assert.True(await table.TryUpdateAsync(
            active,
            joining.Version,
            inserted.Version,
            ct));

        var updated = await table.ReadOrCreateAsync(ct);
        AssertEntryEqual(active, Assert.Single(updated.Entries));
    });

    [Fact]
    public Task GenerationsAreMonotonicWithinOneClusterIncarnation() => RunAsync(async (table, ct) =>
    {
        var first = await table.AllocateGenerationAsync(BuildTag, ct);
        var second = await table.AllocateGenerationAsync(BuildTag, ct);

        Assert.Equal(first.Cluster, second.Cluster);
        Assert.Equal(first.Value + 1, second.Value);
    });

    [Fact]
    public Task FirstGenerationEstablishesBuildTagAndRejectsAnotherBuildTag() => RunAsync(async (table, ct) =>
    {
        await table.AllocateGenerationAsync(BuildTag, ct);

        var exception = await Assert.ThrowsAsync<ClusterMembershipFencedException>(() =>
            table.AllocateGenerationAsync("Release2", ct).AsTask());

        Assert.Contains("Release1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Release2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(BuildTag, (await table.ReadOrCreateAsync(ct)).BuildTag);
    });

    [Fact]
    public Task ConcurrentWritersCannotCommitTheSameTableVersion() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var first = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var second = Joining(initial.Cluster, "server-2", "22222222-2222-2222-2222-222222222222");

        var writes = await Task.WhenAll(
            table.TryInsertAsync(first, initial.Version, ct).AsTask(),
            table.TryInsertAsync(second, initial.Version, ct).AsTask());

        Assert.Single(writes, static committed => committed);
        var committed = await table.ReadOrCreateAsync(ct);
        Assert.Equal(new MembershipViewId(1), committed.Version);
        Assert.Single(committed.Entries);
    });

    [Fact]
    public Task DuplicateReferenceWithFreshViewIsRejectedWithoutMutation() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(joining, initial.Version, ct));
        var beforeDuplicate = await table.ReadOrCreateAsync(ct);

        Assert.False(await table.TryInsertAsync(joining, beforeDuplicate.Version, ct));

        var afterDuplicate = await table.ReadOrCreateAsync(ct);
        Assert.Equal(beforeDuplicate.Version, afterDuplicate.Version);
        AssertEntryEqual(joining, Assert.Single(afterDuplicate.Entries));
    });

    [Fact]
    public Task ConcurrentUpdatesEventuallyCommitOneWriterPerTableVersion() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(joining, initial.Version, ct));

        const int writerCount = 20;
        await Task.WhenAll(Enumerable.Range(0, writerCount).Select(async _ =>
        {
            while (true)
            {
                var snapshot = await table.ReadOrCreateAsync(ct);
                var current = Assert.Single(snapshot.Entries);
                if (await table.TryUpdateAsync(
                    current.WithStatus(MembershipTableStatus.Joining),
                    current.Version,
                    snapshot.Version,
                    ct))
                    return;
            }
        }));

        var committed = await table.ReadOrCreateAsync(ct);
        Assert.Equal(writerCount + 1, committed.Version.Value);
        Assert.Equal(writerCount + 1, Assert.Single(committed.Entries).Version);
    });

    [Fact]
    public Task StaleEntryVersionIsRejectedWithoutMutation() => RunAsync(async (table, ct) =>
    {
        var (active, current, _) = await CreateActiveEntryAsync(table, ct);

        Assert.False(await table.TryUpdateAsync(
            active,
            expectedEntryVersion: active.Version - 1,
            current.Version,
            ct));

        var afterRejectedUpdate = await table.ReadOrCreateAsync(ct);
        Assert.Equal(current.Version, afterRejectedUpdate.Version);
        AssertEntryEqual(active, Assert.Single(afterRejectedUpdate.Entries));
    });

    [Fact]
    public Task StaleMembershipViewIsRejectedWithoutMutation() => RunAsync(async (table, ct) =>
    {
        var (active, current, staleView) = await CreateActiveEntryAsync(table, ct);
        var proposal = active.WithStatus(MembershipTableStatus.Active);

        Assert.False(await table.TryUpdateAsync(
            proposal,
            active.Version,
            staleView,
            ct));

        var afterRejectedUpdate = await table.ReadOrCreateAsync(ct);
        Assert.Equal(current.Version, afterRejectedUpdate.Version);
        AssertEntryEqual(active, Assert.Single(afterRejectedUpdate.Entries));
    });

    [Fact]
    public Task AStableNodeCannotRejoinUntilItsPreviousIncarnationIsDead() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var first = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var restarted = Joining(initial.Cluster, "server-1", "22222222-2222-2222-2222-222222222222");

        Assert.True(await table.TryInsertAsync(first, initial.Version, ct));
        var afterInsert = await table.ReadOrCreateAsync(ct);
        Assert.False(await table.TryInsertAsync(restarted, afterInsert.Version, ct));
        Assert.True(await table.TryUpdateAsync(
            first.WithStatus(MembershipTableStatus.Dead),
            first.Version,
            afterInsert.Version,
            ct));
        var afterDeath = await table.ReadOrCreateAsync(ct);
        Assert.True(await table.TryInsertAsync(restarted, afterDeath.Version, ct));
    });

    [Fact]
    public Task ReplacementFencesOldIncarnationAndInsertsNewIncarnationInOneTableVersion() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var first = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        var replacement = Joining(
            initial.Cluster,
            "server-1",
            "22222222-2222-2222-2222-222222222222",
            generation: 2);
        Assert.True(await table.TryInsertAsync(first, initial.Version, ct));
        var beforeReplacement = await table.ReadOrCreateAsync(ct);

        Assert.True(await table.TryReplaceAsync(
            first.Reference,
            first.Version,
            replacement,
            beforeReplacement.Version,
            ct));

        var committed = await table.ReadOrCreateAsync(ct);
        Assert.Equal(beforeReplacement.Version.Value + 1, committed.Version.Value);
        Assert.Equal(
            MembershipTableStatus.Dead,
            committed.Entries.Single(entry => entry.Reference == first.Reference).Status);
        Assert.Equal(
            MembershipTableStatus.Joining,
            committed.Entries.Single(entry => entry.Reference == replacement.Reference).Status);
    });

    [Fact]
    public Task DeadIsFinalAndRejectsHeartbeats() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(joining, initial.Version, ct));
        var inserted = await table.ReadOrCreateAsync(ct);
        var dead = joining.WithStatus(MembershipTableStatus.Dead);
        Assert.True(await table.TryUpdateAsync(dead, joining.Version, inserted.Version, ct));
        var afterDeath = await table.ReadOrCreateAsync(ct);

        Assert.False(await table.TryUpdateAsync(
            dead.WithStatus(MembershipTableStatus.Active),
            dead.Version,
            afterDeath.Version,
            ct));
        Assert.False(await table.TryUpdateIAmAliveAsync(
            dead.Reference,
            dead.IAmAliveTime.AddSeconds(1),
            ct));
        Assert.Equal(afterDeath.Version, (await table.ReadOrCreateAsync(ct)).Version);
    });

    [Fact]
    public Task StoppingCannotReturnToActive() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(joining, initial.Version, ct));
        var inserted = await table.ReadOrCreateAsync(ct);
        var active = joining.WithStatus(MembershipTableStatus.Active);
        Assert.True(await table.TryUpdateAsync(active, joining.Version, inserted.Version, ct));
        var activeSnapshot = await table.ReadOrCreateAsync(ct);
        var stopping = active.WithStatus(MembershipTableStatus.Stopping);
        Assert.True(await table.TryUpdateAsync(stopping, active.Version, activeSnapshot.Version, ct));
        var stoppingSnapshot = await table.ReadOrCreateAsync(ct);

        Assert.False(await table.TryUpdateAsync(
            stopping.WithStatus(MembershipTableStatus.Active),
            stopping.Version,
            stoppingSnapshot.Version,
            ct));
        Assert.Equal(
            MembershipTableStatus.Stopping,
            Assert.Single((await table.ReadOrCreateAsync(ct)).Entries).Status);
    });

    [Fact]
    public Task HeartbeatRefreshesLivenessWithoutCreatingAClusterWideChange() => RunAsync(async (table, ct) =>
    {
        var initial = await table.ReadOrCreateAsync(ct);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(joining, initial.Version, ct));
        var inserted = await table.ReadOrCreateAsync(ct);
        var refreshedAt = joining.IAmAliveTime.AddSeconds(1);

        Assert.True(await table.TryUpdateIAmAliveAsync(joining.Reference, refreshedAt, ct));

        var refreshed = await table.ReadOrCreateAsync(ct);
        Assert.Equal(inserted.Version, refreshed.Version);
        Assert.Equal(refreshedAt, Assert.Single(refreshed.Entries).IAmAliveTime);
        Assert.Equal(joining.Version, Assert.Single(refreshed.Entries).Version);
    });

    [Fact]
    public Task DefunctCleanupIsBoundedAndDoesNotCreateAMembershipView() => RunAsync(async (table, ct) =>
    {
        var snapshot = await table.ReadOrCreateAsync(ct);
        foreach (var entry in new[]
                 {
                     Joining(snapshot.Cluster, "server-1", "11111111-1111-1111-1111-111111111111"),
                     Joining(snapshot.Cluster, "server-2", "22222222-2222-2222-2222-222222222222")
                 })
        {
            Assert.True(await table.TryInsertAsync(entry, snapshot.Version, ct));
            snapshot = await table.ReadOrCreateAsync(ct);
            Assert.True(await table.TryUpdateAsync(
                entry.WithStatus(MembershipTableStatus.Dead),
                entry.Version,
                snapshot.Version,
                ct));
            snapshot = await table.ReadOrCreateAsync(ct);
        }

        var viewBeforeCleanup = snapshot.Version;
        Assert.Equal(1, await table.CleanupDefunctAsync(
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            maximumRows: 1,
            ct));

        var afterFirstPass = await table.ReadOrCreateAsync(ct);
        Assert.Equal(viewBeforeCleanup, afterFirstPass.Version);
        Assert.Single(afterFirstPass.Entries);
        Assert.Equal(1, await table.CleanupDefunctAsync(
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            maximumRows: 1,
            ct));
        Assert.Empty((await table.ReadOrCreateAsync(ct)).Entries);
    });

    [Fact]
    public Task DefunctCleanupOnlyRemovesDeadRowsStrictlyBeforeTheCutoff() => RunAsync(async (table, ct) =>
    {
        var cutoff = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var old = cutoff.AddMinutes(-1);
        var recent = cutoff.AddMinutes(1);
        var snapshot = await table.ReadOrCreateAsync(ct);

        snapshot = await InsertAtStatusAsync(
            table, snapshot, "active-old", "11111111-1111-1111-1111-111111111111", old,
            MembershipTableStatus.Active, ct);
        snapshot = await InsertAtStatusAsync(
            table, snapshot, "dead-at-cutoff", "22222222-2222-2222-2222-222222222222", cutoff,
            MembershipTableStatus.Dead, ct);
        snapshot = await InsertAtStatusAsync(
            table, snapshot, "dead-old", "33333333-3333-3333-3333-333333333333", old,
            MembershipTableStatus.Dead, ct);
        snapshot = await InsertAtStatusAsync(
            table, snapshot, "dead-recent", "44444444-4444-4444-4444-444444444444", recent,
            MembershipTableStatus.Dead, ct);
        snapshot = await InsertAtStatusAsync(
            table, snapshot, "joining-old", "55555555-5555-5555-5555-555555555555", old,
            MembershipTableStatus.Joining, ct);
        snapshot = await InsertAtStatusAsync(
            table, snapshot, "stopping-old", "66666666-6666-6666-6666-666666666666", old,
            MembershipTableStatus.Stopping, ct);

        var viewBeforeCleanup = snapshot.Version;
        Assert.Equal(1, await table.CleanupDefunctAsync(cutoff, maximumRows: 100, ct));

        var afterCleanup = await table.ReadOrCreateAsync(ct);
        Assert.Equal(viewBeforeCleanup, afterCleanup.Version);
        Assert.Equal(
            ["active-old", "dead-at-cutoff", "dead-recent", "joining-old", "stopping-old"],
            afterCleanup.Entries.Select(static entry => entry.Reference.Node.Value));
    });

    private async Task RunAsync(Func<IMembershipTable, CancellationToken, Task> test)
    {
        var table = await CreateTableAsync();
        if (table is null) return;
        try
        {
            await test(
                table,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            await DisposeTableAsync(table);
        }
    }

    private protected virtual async ValueTask DisposeTableAsync(IMembershipTable table)
    {
        if (table is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    private static async Task<(
        MembershipTableEntry Active,
        MembershipTableSnapshot Current,
        MembershipViewId PreviousView)>
        CreateActiveEntryAsync(IMembershipTable table, CancellationToken cancellationToken)
    {
        var initial = await table.ReadOrCreateAsync(cancellationToken);
        var joining = Joining(initial.Cluster, "server-1", "11111111-1111-1111-1111-111111111111");
        Assert.True(await table.TryInsertAsync(joining, initial.Version, cancellationToken));
        var inserted = await table.ReadOrCreateAsync(cancellationToken);
        var active = joining.WithStatus(MembershipTableStatus.Active);
        Assert.True(await table.TryUpdateAsync(active, joining.Version, inserted.Version, cancellationToken));
        return (active, await table.ReadOrCreateAsync(cancellationToken), inserted.Version);
    }

    private static async Task<MembershipTableSnapshot> InsertAtStatusAsync(
        IMembershipTable table,
        MembershipTableSnapshot snapshot,
        string node,
        string incarnation,
        DateTimeOffset iAmAliveTime,
        MembershipTableStatus status,
        CancellationToken cancellationToken)
    {
        var entry = Joining(snapshot.Cluster, node, incarnation, iAmAliveTime: iAmAliveTime);
        Assert.True(await table.TryInsertAsync(entry, snapshot.Version, cancellationToken));
        snapshot = await table.ReadOrCreateAsync(cancellationToken);
        if (status == MembershipTableStatus.Joining) return snapshot;

        if (status is MembershipTableStatus.Active or MembershipTableStatus.Stopping)
        {
            var active = entry.WithStatus(MembershipTableStatus.Active);
            Assert.True(await table.TryUpdateAsync(
                active, entry.Version, snapshot.Version, cancellationToken));
            entry = active;
            snapshot = await table.ReadOrCreateAsync(cancellationToken);
            if (status == MembershipTableStatus.Active) return snapshot;
        }

        var target = entry.WithStatus(status);
        Assert.True(await table.TryUpdateAsync(
            target, entry.Version, snapshot.Version, cancellationToken));
        return await table.ReadOrCreateAsync(cancellationToken);
    }

    private static MembershipTableEntry RichJoining(ClusterIncarnationId cluster) =>
        new(
            new NodeReference(
                cluster,
                new NodeId("数据节点-一"),
                new NodeIncarnationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))),
            MembershipTableStatus.Joining,
            new NodeEndpoint(
                "tcp://127.0.0.1:21001",
                new Dictionary<string, string>
                {
                    ["network"] = "内网",
                    ["zone"] = "上海-1"
                }),
            version: 1,
            iAmAliveTime: DateTimeOffset.Parse("2026-08-24T01:02:03Z"),
            labels: new Dictionary<string, string>
            {
                ["region"] = "华东",
                ["role"] = "数据"
            },
            actorHosts:
            [
                new NodeActorHostDescriptor(
                    "房间Actor",
                    "策略-α",
                    "热更-一",
                    new Dictionary<string, string> { ["能力"] = "战斗" })
            ],
            startupActors:
            [
                new StartupActorDescriptor(
                    "排行榜启动Actor",
                    "策略-β",
                    "热更-二",
                    new Dictionary<string, string> { ["用途"] = "初始化" })
            ],
            suspectVotes:
            [
                new MembershipSuspectVote(
                    new NodeReference(
                        cluster,
                        new NodeId("观察节点-一"),
                        new NodeIncarnationId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))),
                    DateTimeOffset.Parse("2026-08-24T01:02:01Z")),
                new MembershipSuspectVote(
                    new NodeReference(
                        cluster,
                        new NodeId("观察节点-二"),
                        new NodeIncarnationId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"))),
                    DateTimeOffset.Parse("2026-08-24T01:02:02Z"))
            ],
            startTime: DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            generation: 42);

    private static void AssertEntryEqual(MembershipTableEntry expected, MembershipTableEntry actual)
    {
        Assert.Equal(expected.Reference, actual.Reference);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.ClusterEndpoint.Address, actual.ClusterEndpoint.Address);
        AssertDictionaryEqual(expected.ClusterEndpoint.Metadata, actual.ClusterEndpoint.Metadata);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.IAmAliveTime, actual.IAmAliveTime);
        Assert.Equal(expected.StartTime, actual.StartTime);
        Assert.Equal(expected.Generation, actual.Generation);
        AssertDictionaryEqual(expected.Labels, actual.Labels);
        AssertDescriptorListsEqual(expected.ActorHosts, actual.ActorHosts);
        AssertDescriptorListsEqual(expected.StartupActors, actual.StartupActors);
        Assert.Equal(expected.SuspectVotes.Count, actual.SuspectVotes.Count);
        for (var index = 0; index < expected.SuspectVotes.Count; index++)
        {
            Assert.Equal(expected.SuspectVotes[index].Observer, actual.SuspectVotes[index].Observer);
            Assert.Equal(expected.SuspectVotes[index].Timestamp, actual.SuspectVotes[index].Timestamp);
        }
    }

    private static void AssertDescriptorListsEqual(
        IReadOnlyList<NodeActorHostDescriptor> expected,
        IReadOnlyList<NodeActorHostDescriptor> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Actor, actual[index].Actor);
            Assert.Equal(expected[index].PolicyHash, actual[index].PolicyHash);
            Assert.Equal(expected[index].HotfixVersion, actual[index].HotfixVersion);
            AssertDictionaryEqual(expected[index].Metadata, actual[index].Metadata);
        }
    }

    private static void AssertDescriptorListsEqual(
        IReadOnlyList<StartupActorDescriptor> expected,
        IReadOnlyList<StartupActorDescriptor> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Actor, actual[index].Actor);
            Assert.Equal(expected[index].PolicyHash, actual[index].PolicyHash);
            Assert.Equal(expected[index].HotfixVersion, actual[index].HotfixVersion);
            AssertDictionaryEqual(expected[index].Metadata, actual[index].Metadata);
        }
    }

    private static void AssertDictionaryEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual) =>
        Assert.Equal(
            expected.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            actual.OrderBy(static pair => pair.Key, StringComparer.Ordinal));

    private static MembershipTableEntry Joining(
        ClusterIncarnationId cluster,
        string node,
        string incarnation,
        long generation = 1,
        DateTimeOffset? iAmAliveTime = null) =>
        new(
            new NodeReference(cluster, new NodeId(node), new NodeIncarnationId(Guid.Parse(incarnation))),
            MembershipTableStatus.Joining,
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            version: 1,
            iAmAliveTime: iAmAliveTime ?? DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            generation: generation);
}
