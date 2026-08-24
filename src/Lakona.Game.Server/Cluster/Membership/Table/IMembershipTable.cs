namespace Lakona.Game.Cluster.Membership;

internal interface IMembershipTable
{
    ValueTask<MembershipTableGeneration> AllocateGenerationAsync(string clusterId, CancellationToken cancellationToken = default);

    ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(string clusterId, CancellationToken cancellationToken = default);

    ValueTask<bool> TryInsertAsync(string clusterId, MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default);

    ValueTask<bool> TryReplaceAsync(string clusterId, NodeReference previous, long expectedPreviousVersion, MembershipTableEntry replacement, MembershipViewId expectedVersion, CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpdateAsync(string clusterId, MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpdateIAmAliveAsync(string clusterId, NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    ValueTask<int> CleanupDefunctAsync(string clusterId, DateTimeOffset before, int maximumRows, CancellationToken cancellationToken = default);
}

internal readonly record struct MembershipTableGeneration(
    ClusterIncarnationId Cluster,
    long Value);
