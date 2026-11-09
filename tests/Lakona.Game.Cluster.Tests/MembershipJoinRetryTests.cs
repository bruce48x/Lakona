using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class MembershipJoinRetryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAllocationCannotBeRetriedByTheSameIncarnation(bool committed)
    {
        var table = new FaultTable();
        var failure = new IOException("Allocation response unavailable.");
        if (committed) table.AfterAllocate = () => throw failure;
        else table.BeforeAllocate = () => throw failure;
        var (manager, state) = CreateManager(table);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => manager.JoinAsync(Token).AsTask()));
        table.BeforeAllocate = null;
        table.AfterAllocate = null;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => manager.JoinAsync(Token).AsTask()));
        Assert.Equal(1, table.Allocations);
        Assert.Empty((await table.Inner.ReadOrCreateAsync(Token)).Entries);
        Assert.Throws<InvalidOperationException>(() => state.Current);
        Assert.Equal(committed ? 2 : 1, (await table.Inner.AllocateGenerationAsync("Test", Token)).Value);
    }

    [Fact]
    public async Task ReadFailureDoesNotLetAnOlderJoinReplaceANewerProcess()
    {
        var table = new FaultTable();
        var (older, state) = CreateManager(table);
        table.BeforeRead = () => throw new IOException("Read failed after allocation.");
        await Assert.ThrowsAsync<IOException>(() => older.JoinAsync(Token).AsTask());
        table.BeforeRead = null;
        var (newer, _) = CreateManager(table.Inner);
        var current = await newer.JoinAsync(Token);
        await newer.ActivateAsync(null, [], [], Token);

        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => older.JoinAsync(Token).AsTask());
        Assert.Equal(1, table.Allocations);
        var row = Assert.Single((await table.Inner.ReadOrCreateAsync(Token)).Entries);
        Assert.Equal(current, row.Reference);
        Assert.Equal(2, row.Generation);
        Assert.Equal(MembershipTableStatus.Active, row.Status);
        Assert.Throws<InvalidOperationException>(() => state.Current);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CommittedJoinSurvivesLostWriteResponseOrConfirmation(bool replacement, bool lostResponse)
    {
        var table = new FaultTable();
        if (replacement)
        {
            var (previous, _) = CreateManager(table.Inner);
            await previous.JoinAsync(Token);
            await previous.ActivateAsync(null, [], [], Token);
        }
        var (manager, state) = CreateManager(table);
        table.AfterWrite = () =>
        {
            if (lostResponse) throw new IOException("Write committed, response lost.");
            table.BeforeRead = () => throw new IOException("Confirmation read failed.");
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<IOException>(() => manager.JoinAsync(Token).AsTask());
        table.AfterWrite = null;
        table.BeforeRead = null;
        var committed = await table.Inner.ReadOrCreateAsync(Token);
        var joining = Assert.Single(committed.Entries, entry => entry.Status == MembershipTableStatus.Joining);

        Assert.Equal(joining.Reference, await manager.JoinAsync(Token));
        Assert.Equal(1, table.Allocations);
        Assert.Equal(1, table.Writes);
        Assert.Equal(committed.Version, state.Current.View);
        await manager.ActivateAsync(null, [], [], Token);
        Assert.Equal(joining.Generation, (await table.Inner.ReadOrCreateAsync(Token)).Entries
            .Single(entry => entry.Reference == manager.Local).Generation);
    }

    [Theory]
    [InlineData("replaced")]
    [InlineData("dead")]
    [InlineData("missing")]
    public async Task ConfirmationCannotPublishAnInvalidLocalRow(string failure)
    {
        var table = new FaultTable();
        var (manager, state) = CreateManager(table);
        NodeReference? successor = null;
        table.AfterWrite = async () =>
        {
            if (failure == "replaced")
            {
                var (newer, _) = CreateManager(table.Inner);
                successor = await newer.JoinAsync(Token);
                await newer.ActivateAsync(null, [], [], Token);
            }
            else
            {
                var snapshot = await table.Inner.ReadOrCreateAsync(Token);
                var own = Assert.Single(snapshot.Entries);
                Assert.True(await table.Inner.TryUpdateAsync(own.WithStatus(MembershipTableStatus.Dead),
                    own.Version, snapshot.Version, Token));
                if (failure == "missing")
                    Assert.Equal(1, await table.Inner.CleanupDefunctAsync(own.IAmAliveTime.AddSeconds(1), 1, Token));
            }
        };

        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => manager.JoinAsync(Token).AsTask());
        table.AfterWrite = null;
        // Fencing is terminal even if the evidence is later cleaned up.
        await table.Inner.CleanupDefunctAsync(DateTimeOffset.MaxValue, 100, Token);
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => manager.JoinAsync(Token).AsTask());
        Assert.Equal(1, table.Allocations);
        Assert.Equal(1, table.Writes);
        Assert.Throws<InvalidOperationException>(() => manager.Local);
        Assert.Throws<InvalidOperationException>(() => state.Current);
        if (successor is not null)
            Assert.Equal(successor, Assert.Single((await table.Inner.ReadOrCreateAsync(Token)).Entries).Reference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClusterResetCannotReuseAnOldGeneration(bool hasSameNamedPeer)
    {
        var table = new FaultTable();
        var (manager, state) = CreateManager(table);
        table.BeforeRead = () => throw new IOException("Pause join after allocation.");
        await Assert.ThrowsAsync<IOException>(() => manager.JoinAsync(Token).AsTask());
        table.BeforeRead = null;
        table.Inner = new InMemoryMembershipTable();
        await table.Inner.AllocateGenerationAsync("Test", Token);
        if (hasSameNamedPeer)
        {
            var (peer, _) = CreateManager(table.Inner);
            await peer.JoinAsync(Token);
            await peer.ActivateAsync(null, [], [], Token);
        }
        var before = await table.Inner.ReadOrCreateAsync(Token);

        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => manager.JoinAsync(Token).AsTask());
        Assert.Equal(1, table.Allocations);
        Assert.Equal(0, table.Writes);
        Assert.Equal(before.Version, (await table.Inner.ReadOrCreateAsync(Token)).Version);
        Assert.Throws<InvalidOperationException>(() => state.Current);
    }

    [Fact]
    public async Task ConflictingOrFailedWritesRetryWithTheOriginalGeneration()
    {
        var table = new FaultTable { RejectedWrites = 2 };
        var (manager, _) = CreateManager(table);
        table.BeforeWrite = () => throw new IOException("Write failed before commit.");
        await Assert.ThrowsAsync<IOException>(() => manager.JoinAsync(Token).AsTask());
        table.BeforeWrite = null;

        var reference = await manager.JoinAsync(Token);
        Assert.Equal(1, table.Allocations);
        Assert.Equal(4, table.Writes);
        var row = Assert.Single((await table.Inner.ReadOrCreateAsync(Token)).Entries);
        Assert.Equal(reference, row.Reference);
        Assert.Equal(1, row.Generation);
    }

    [Fact]
    public async Task LateAllocationAfterCancellationCannotJoinOrAllocateAgain()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var table = new FaultTable { AfterAllocate = cancellation.Cancel };
        var (manager, state) = CreateManager(table);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.JoinAsync(cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.JoinAsync(Token).AsTask());
        Assert.Equal(1, table.Allocations);
        Assert.Equal(0, table.Writes);
        Assert.Throws<InvalidOperationException>(() => state.Current);
    }

    private static (MembershipTableManager Manager, ClusterMembershipState State) CreateManager(IMembershipTable table)
    {
        var state = new ClusterMembershipState();
        return (new MembershipTableManager(new NodeId("worker"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21001"), new ClusterBuildTag("Test"), table, state), state);
    }

    private sealed class FaultTable : IMembershipTable
    {
        public IMembershipTable Inner { get; set; } = new InMemoryMembershipTable();
        public Action? BeforeAllocate { get; set; }
        public Action? AfterAllocate { get; set; }
        public Action? BeforeRead { get; set; }
        public Func<Task>? AfterWrite { get; set; }
        public Action? BeforeWrite { get; set; }
        public int RejectedWrites { get; set; }
        public int Allocations { get; private set; }
        public int Writes { get; private set; }
        public async ValueTask<MembershipTableGeneration> AllocateGenerationAsync(string buildTag, CancellationToken cancellationToken = default)
        {
            Allocations++;
            BeforeAllocate?.Invoke();
            var result = await Inner.AllocateGenerationAsync(buildTag, cancellationToken);
            AfterAllocate?.Invoke();
            return result;
        }
        public ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(CancellationToken cancellationToken = default)
        {
            BeforeRead?.Invoke();
            return Inner.ReadOrCreateAsync(cancellationToken);
        }
        public async ValueTask<bool> TryInsertAsync(MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default)
        {
            Writes++;
            BeforeWrite?.Invoke();
            if (RejectedWrites > 0)
            {
                RejectedWrites--;
                return false;
            }
            var result = await Inner.TryInsertAsync(entry, expectedVersion, cancellationToken);
            if (result && AfterWrite is not null) await AfterWrite();
            return result;
        }
        public async ValueTask<bool> TryReplaceAsync(NodeReference previous, long expectedPreviousVersion, MembershipTableEntry replacement, MembershipViewId expectedVersion, CancellationToken cancellationToken = default)
        {
            Writes++;
            var result = await Inner.TryReplaceAsync(previous, expectedPreviousVersion, replacement, expectedVersion, cancellationToken);
            if (result && AfterWrite is not null) await AfterWrite();
            return result;
        }
        public ValueTask<bool> TryUpdateAsync(MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Inner.TryUpdateAsync(entry, expectedEntryVersion, expectedVersion, cancellationToken);
        public ValueTask<bool> TryUpdateIAmAliveAsync(NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
            Inner.TryUpdateIAmAliveAsync(reference, timestamp, cancellationToken);
        public ValueTask<int> CleanupDefunctAsync(DateTimeOffset before, int maximumRows, CancellationToken cancellationToken = default) =>
            Inner.CleanupDefunctAsync(before, maximumRows, cancellationToken);
    }
}
