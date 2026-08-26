namespace Lakona.Game.Cluster.Membership;

internal interface IMembershipTable
{
    ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
        string buildTag,
        CancellationToken cancellationToken = default);

    ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> TryInsertAsync(MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default);

    ValueTask<bool> TryReplaceAsync(NodeReference previous, long expectedPreviousVersion, MembershipTableEntry replacement, MembershipViewId expectedVersion, CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpdateAsync(MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpdateIAmAliveAsync(NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    ValueTask<int> CleanupDefunctAsync(DateTimeOffset before, int maximumRows, CancellationToken cancellationToken = default);
}

internal readonly record struct MembershipTableGeneration(
    ClusterIncarnationId Cluster,
    long Value);
