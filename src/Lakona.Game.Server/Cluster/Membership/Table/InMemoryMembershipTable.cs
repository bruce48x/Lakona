namespace Lakona.Game.Cluster.Membership;

internal sealed class InMemoryMembershipTable : IMembershipTable
{
    private readonly object gate = new();
    private readonly Dictionary<string, ClusterRows> clusters = new(StringComparer.Ordinal);

    public ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
        string clusterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        lock (gate)
        {
            if (!clusters.TryGetValue(clusterId, out var rows))
            {
                rows = new ClusterRows(ClusterIncarnationId.New());
                clusters.Add(clusterId, rows);
            }

            if (rows.NextGeneration == long.MaxValue)
            {
                throw new InvalidOperationException("Membership generation is exhausted.");
            }

            var allocated = rows.NextGeneration++;
            return new ValueTask<MembershipTableGeneration>(new MembershipTableGeneration(rows.Cluster, allocated));
        }
    }

    public ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(string clusterId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        lock (gate)
        {
            if (!clusters.TryGetValue(clusterId, out var rows))
            {
                rows = new ClusterRows(ClusterIncarnationId.New());
                clusters.Add(clusterId, rows);
            }

            return new ValueTask<MembershipTableSnapshot>(CreateSnapshot(clusterId, rows));
        }
    }

    public ValueTask<bool> TryInsertAsync(string clusterId, MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            if (!TryGetMatchingCluster(clusterId, entry.Reference.Cluster, expectedVersion, out var rows)
                || entry.Version != 1
                || entry.Status != MembershipTableStatus.Joining
                || rows.Entries.ContainsKey(entry.Reference)
                || rows.Entries.Values.Any(candidate => candidate.Reference.Node == entry.Reference.Node && candidate.Status != MembershipTableStatus.Dead))
            {
                return new ValueTask<bool>(false);
            }

            rows.Entries.Add(entry.Reference, entry);
            rows.Version = Next(rows.Version);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> TryUpdateAsync(string clusterId, MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            if (!TryGetMatchingCluster(clusterId, entry.Reference.Cluster, expectedVersion, out var rows)
                || !rows.Entries.TryGetValue(entry.Reference, out var current)
                || current.Version != expectedEntryVersion
                || current.Generation != entry.Generation
                || expectedEntryVersion == long.MaxValue
                || entry.Version != expectedEntryVersion + 1
                || !IsValidTransition(current.Status, entry.Status))
            {
                return new ValueTask<bool>(false);
            }

            rows.Entries[entry.Reference] = entry;
            rows.Version = Next(rows.Version);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> TryReplaceAsync(
        string clusterId,
        NodeReference previous,
        long expectedPreviousVersion,
        MembershipTableEntry replacement,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        lock (gate)
        {
            if (!TryGetMatchingCluster(clusterId, replacement.Reference.Cluster, expectedVersion, out var rows)
                || !rows.Entries.TryGetValue(previous, out var current)
                || current.Version != expectedPreviousVersion
                || current.Version == long.MaxValue
                || current.Status == MembershipTableStatus.Dead
                || current.Reference.Node != replacement.Reference.Node
                || current.Reference.Cluster != replacement.Reference.Cluster
                || current.Reference == replacement.Reference
                || replacement.Generation <= current.Generation
                || replacement.Version != 1
                || replacement.Status != MembershipTableStatus.Joining
                || rows.Entries.ContainsKey(replacement.Reference))
            {
                return new ValueTask<bool>(false);
            }

            rows.Entries[previous] = current.WithStatus(MembershipTableStatus.Dead);
            rows.Entries.Add(replacement.Reference, replacement);
            rows.Version = Next(rows.Version);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> TryUpdateIAmAliveAsync(string clusterId, NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        ArgumentNullException.ThrowIfNull(reference);
        lock (gate)
        {
            if (!clusters.TryGetValue(clusterId, out var rows)
                || rows.Cluster != reference.Cluster
                || !rows.Entries.TryGetValue(reference, out var current)
                || current.Status == MembershipTableStatus.Dead
                || timestamp <= current.IAmAliveTime)
            {
                return new ValueTask<bool>(false);
            }

            rows.Entries[reference] = current.WithIAmAliveTime(timestamp);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<int> CleanupDefunctAsync(
        string clusterId,
        DateTimeOffset before,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateClusterId(clusterId);
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        lock (gate)
        {
            if (!clusters.TryGetValue(clusterId, out var rows)) return new ValueTask<int>(0);
            var expired = rows.Entries.Values
                .Where(entry => entry.Status == MembershipTableStatus.Dead && entry.IAmAliveTime < before)
                .OrderBy(static entry => entry.IAmAliveTime)
                .Take(maximumRows)
                .Select(static entry => entry.Reference)
                .ToArray();
            foreach (var reference in expired) rows.Entries.Remove(reference);
            return new ValueTask<int>(expired.Length);
        }
    }

    private bool TryGetMatchingCluster(string clusterId, ClusterIncarnationId incarnation, MembershipViewId expectedVersion, out ClusterRows rows) =>
        clusters.TryGetValue(clusterId, out rows!) && rows.Cluster == incarnation && rows.Version == expectedVersion;

    private static bool IsValidTransition(MembershipTableStatus current, MembershipTableStatus next) => current switch
    {
        MembershipTableStatus.Joining => next is MembershipTableStatus.Joining or MembershipTableStatus.Active or MembershipTableStatus.Dead,
        MembershipTableStatus.Active => next is MembershipTableStatus.Active or MembershipTableStatus.Stopping or MembershipTableStatus.Dead,
        MembershipTableStatus.Stopping => next is MembershipTableStatus.Stopping or MembershipTableStatus.Dead,
        MembershipTableStatus.Dead => false,
        _ => false
    };

    private static MembershipViewId Next(MembershipViewId current)
    {
        if (current.Value == long.MaxValue)
        {
            throw new InvalidOperationException("Membership table version is exhausted.");
        }

        return new MembershipViewId(current.Value + 1);
    }

    private static MembershipTableSnapshot CreateSnapshot(string clusterId, ClusterRows rows) =>
        new(clusterId, rows.Cluster, rows.Version, rows.Entries.Values.ToArray());

    private static void ValidateClusterId(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            throw new ArgumentException("Cluster id is required.", nameof(clusterId));
        }
    }

    private sealed class ClusterRows(ClusterIncarnationId cluster)
    {
        public ClusterIncarnationId Cluster { get; } = cluster;
        public MembershipViewId Version { get; set; } = new(0);
        public long NextGeneration { get; set; } = 1;
        public Dictionary<NodeReference, MembershipTableEntry> Entries { get; } = [];
    }
}
