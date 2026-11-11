using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public abstract partial class MembershipTableContractTests
{
    [Fact]
    public Task CapacityAdmissionPreservesRefreshReplacementAndCommitConfirmation() => RunAsync(async (table, ct) =>
    {
        var observer = CapacityManager(table, "observer");
        await observer.JoinAsync(ct);
        await observer.ActivateAsync(null, [], [], ct);
        var snapshot = await table.ReadOrCreateAsync(ct);
        var version = snapshot.Version;
        // Populate real storage without repeatedly materializing the entire table.
        for (var i = 1; i < ClusterMembershipSnapshot.MaximumMembersV1 - 1; i++)
        {
            var generation = await table.AllocateGenerationAsync(BuildTag, ct);
            var entry = new MembershipTableEntry(
                new NodeReference(generation.Cluster, new NodeId($"capacity-{i}"), NodeIncarnationId.New()),
                MembershipTableStatus.Joining, new NodeEndpoint("tcp://127.0.0.1:21001"),
                version: 1, iAmAliveTime: DateTimeOffset.UtcNow, generation: generation.Value);
            Assert.True(await table.TryInsertAsync(entry, version, ct));
            version = new MembershipViewId(version.Value + 1);
        }
        await observer.RefreshAsync(ct);

        var contested = new CapacityTable(table) { SynchronizeFirstTwoInserts = true };
        var candidates = new[] { CapacityManager(contested, "first"), CapacityManager(contested, "second") };
        var errors = await Task.WhenAll(candidates.Select(async manager =>
            await Record.ExceptionAsync(() => manager.JoinAsync(ct).AsTask())));
        Assert.Single(errors, error => error is null);
        var denied = Assert.IsType<ClusterMembershipFencedException>(Assert.Single(errors, error => error is not null));
        Assert.Contains("1024", denied.Message);
        Assert.Equal(2, contested.InsertAttempts); // One CAS winner, one stale write; no over-capacity retry write.
        var full = await table.ReadOrCreateAsync(ct);
        Assert.Equal(version.Value + 1, full.Version.Value);
        Assert.Equal(ClusterMembershipSnapshot.MaximumMembersV1, full.Entries.Count);
        await observer.RefreshAsync(ct);

        // Replacing an Active member at capacity consumes no additional slot.
        var activeReplacement = CapacityManager(table, "observer");
        await activeReplacement.JoinAsync(ct);
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => observer.RefreshAsync(ct).AsTask());
        await activeReplacement.RefreshAsync(ct);

        // Replacing a Joining member also preserves capacity. A lost successful
        // write response must be confirmed before considering any new admission.
        var winner = candidates[Array.FindIndex(errors, error => error is null)];
        var lostResponse = new CapacityTable(table) { LoseNextWriteResponse = true };
        var confirmed = CapacityManager(lostResponse, winner.Local.Node.Value);
        await Assert.ThrowsAsync<IOException>(() => confirmed.JoinAsync(ct).AsTask());
        var committed = await table.ReadOrCreateAsync(ct);
        await confirmed.JoinAsync(ct);
        Assert.Equal(committed.Version, (await table.ReadOrCreateAsync(ct)).Version);
        await confirmed.RefreshAsync(ct);

        // Stopping leaves the projection. Its slot may already belong to a new
        // node, so replacing that stopped incarnation must count as a new slot.
        await activeReplacement.ActivateAsync(null, [], [], ct);
        await activeReplacement.MarkStoppingAsync(ct);
        var filler = CapacityManager(table, "filler");
        await filler.JoinAsync(ct);
        var beforeRejectedReplacement = await table.ReadOrCreateAsync(ct);
        var stoppedReplacement = CapacityManager(table, "observer");
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => stoppedReplacement.JoinAsync(ct).AsTask());
        var unchanged = await table.ReadOrCreateAsync(ct);
        Assert.Equal(beforeRejectedReplacement.Version, unchanged.Version);
        Assert.Equal(MembershipTableStatus.Stopping,
            unchanged.Entries.Single(entry => entry.Reference == activeReplacement.Local).Status);
        await confirmed.RefreshAsync(ct);

        await filler.MarkDeadAsync(ct);
        // A rejected process remains rejected even after capacity becomes available.
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => stoppedReplacement.JoinAsync(ct).AsTask());
        var restarted = CapacityManager(table, "observer");
        await restarted.JoinAsync(ct);
        // Dead history does not consume capacity, but restarting it still needs a slot.
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => CapacityManager(table, "filler").JoinAsync(ct).AsTask());
        await confirmed.MarkDeadAsync(ct);
        await CapacityManager(table, "filler").JoinAsync(ct);
        await restarted.RefreshAsync(ct);
        var final = await table.ReadOrCreateAsync(ct);
        Assert.True(final.Entries.Count > ClusterMembershipSnapshot.MaximumMembersV1);
        Assert.Equal(ClusterMembershipSnapshot.MaximumMembersV1,
            new MembershipTableSnapshotProjection(final).Snapshot.Members.Count);
    });

    private static MembershipTableManager CapacityManager(IMembershipTable table, string node) =>
        new(new NodeId(node), NodeIncarnationId.New(), new NodeEndpoint("tcp://127.0.0.1:21001"),
            new ClusterBuildTag(BuildTag), table, new ClusterMembershipState());

    private sealed class CapacityTable(IMembershipTable inner) : IMembershipTable
    {
        private readonly TaskCompletionSource bothInserts = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int insertAttempts;
        public bool SynchronizeFirstTwoInserts { get; init; }
        public bool LoseNextWriteResponse { get; set; }
        public int InsertAttempts => Volatile.Read(ref insertAttempts);
        public ValueTask<MembershipTableGeneration> AllocateGenerationAsync(string buildTag, CancellationToken ct = default) => inner.AllocateGenerationAsync(buildTag, ct);
        public ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(CancellationToken ct = default) => inner.ReadOrCreateAsync(ct);
        public async ValueTask<bool> TryInsertAsync(MembershipTableEntry entry, MembershipViewId version, CancellationToken ct = default)
        {
            var attempt = Interlocked.Increment(ref insertAttempts);
            if (SynchronizeFirstTwoInserts && attempt <= 2)
            {
                if (attempt == 2) bothInserts.TrySetResult();
                await bothInserts.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            return CheckResponse(await inner.TryInsertAsync(entry, version, ct));
        }
        public async ValueTask<bool> TryReplaceAsync(NodeReference previous, long previousVersion, MembershipTableEntry replacement, MembershipViewId version, CancellationToken ct = default) =>
            CheckResponse(await inner.TryReplaceAsync(previous, previousVersion, replacement, version, ct));
        private bool CheckResponse(bool committed)
        {
            if (committed && LoseNextWriteResponse)
            {
                LoseNextWriteResponse = false;
                throw new IOException("Membership write committed, but its response was lost.");
            }
            return committed;
        }
        public ValueTask<bool> TryUpdateAsync(MembershipTableEntry entry, long entryVersion, MembershipViewId version, CancellationToken ct = default) => inner.TryUpdateAsync(entry, entryVersion, version, ct);
        public ValueTask<bool> TryUpdateIAmAliveAsync(NodeReference reference, DateTimeOffset timestamp, CancellationToken ct = default) => inner.TryUpdateIAmAliveAsync(reference, timestamp, ct);
        public ValueTask<int> CleanupDefunctAsync(DateTimeOffset before, int maximumRows, CancellationToken ct = default) => inner.CleanupDefunctAsync(before, maximumRows, ct);
    }
}
