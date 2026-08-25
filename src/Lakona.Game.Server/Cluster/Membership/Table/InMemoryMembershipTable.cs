namespace Lakona.Game.Cluster.Membership;

internal sealed class InMemoryMembershipTable : IMembershipTable
{
    private readonly object gate = new();
    private ClusterRows? rows;

    public ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var current = GetOrCreateRows();

            if (current.NextGeneration == long.MaxValue)
            {
                throw new InvalidOperationException("Membership generation is exhausted.");
            }

            var allocated = current.NextGeneration++;
            return new ValueTask<MembershipTableGeneration>(
                new MembershipTableGeneration(current.Cluster, allocated));
        }
    }

    public ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return new ValueTask<MembershipTableSnapshot>(CreateSnapshot(GetOrCreateRows()));
        }
    }

    public ValueTask<bool> TryInsertAsync(
        MembershipTableEntry entry,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            if (!TryGetMatchingCluster(entry.Reference.Cluster, expectedVersion, out var current)
                || entry.Version != 1
                || entry.Status != MembershipTableStatus.Joining
                || current.Entries.ContainsKey(entry.Reference)
                || current.Entries.Values.Any(candidate =>
                    candidate.Reference.Node == entry.Reference.Node
                    && candidate.Status != MembershipTableStatus.Dead))
            {
                return new ValueTask<bool>(false);
            }

            current.Entries.Add(entry.Reference, entry);
            current.Version = Next(current.Version);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> TryUpdateAsync(
        MembershipTableEntry entry,
        long expectedEntryVersion,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            if (!TryGetMatchingCluster(entry.Reference.Cluster, expectedVersion, out var currentRows)
                || !currentRows.Entries.TryGetValue(entry.Reference, out var currentEntry)
                || currentEntry.Version != expectedEntryVersion
                || currentEntry.Generation != entry.Generation
                || expectedEntryVersion == long.MaxValue
                || entry.Version != expectedEntryVersion + 1
                || !IsValidTransition(currentEntry.Status, entry.Status))
            {
                return new ValueTask<bool>(false);
            }

            currentRows.Entries[entry.Reference] = entry;
            currentRows.Version = Next(currentRows.Version);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> TryReplaceAsync(
        NodeReference previous,
        long expectedPreviousVersion,
        MembershipTableEntry replacement,
        MembershipViewId expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        lock (gate)
        {
            if (!TryGetMatchingCluster(
                    replacement.Reference.Cluster,
                    expectedVersion,
                    out var currentRows)
                || !currentRows.Entries.TryGetValue(previous, out var currentEntry)
                || currentEntry.Version != expectedPreviousVersion
                || currentEntry.Version == long.MaxValue
                || currentEntry.Status == MembershipTableStatus.Dead
                || currentEntry.Reference.Node != replacement.Reference.Node
                || currentEntry.Reference.Cluster != replacement.Reference.Cluster
                || currentEntry.Reference == replacement.Reference
                || replacement.Generation <= currentEntry.Generation
                || replacement.Version != 1
                || replacement.Status != MembershipTableStatus.Joining
                || currentRows.Entries.ContainsKey(replacement.Reference))
            {
                return new ValueTask<bool>(false);
            }

            currentRows.Entries[previous] = currentEntry.WithStatus(MembershipTableStatus.Dead);
            currentRows.Entries.Add(replacement.Reference, replacement);
            currentRows.Version = Next(currentRows.Version);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> TryUpdateIAmAliveAsync(
        NodeReference reference,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(reference);
        lock (gate)
        {
            if (rows is null
                || rows.Cluster != reference.Cluster
                || !rows.Entries.TryGetValue(reference, out var currentEntry)
                || currentEntry.Status == MembershipTableStatus.Dead
                || timestamp <= currentEntry.IAmAliveTime)
            {
                return new ValueTask<bool>(false);
            }

            rows.Entries[reference] = currentEntry.WithIAmAliveTime(timestamp);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<int> CleanupDefunctAsync(
        DateTimeOffset before,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        lock (gate)
        {
            if (rows is null) return new ValueTask<int>(0);
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

    private bool TryGetMatchingCluster(
        ClusterIncarnationId incarnation,
        MembershipViewId expectedVersion,
        out ClusterRows current)
    {
        current = rows!;
        return current is not null
            && current.Cluster == incarnation
            && current.Version == expectedVersion;
    }

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

    private ClusterRows GetOrCreateRows() =>
        rows ??= new ClusterRows(ClusterIncarnationId.New());

    private static MembershipTableSnapshot CreateSnapshot(ClusterRows rows) =>
        new(rows.Cluster, rows.Version, rows.Entries.Values.ToArray());

    private sealed class ClusterRows(ClusterIncarnationId cluster)
    {
        public ClusterIncarnationId Cluster { get; } = cluster;
        public MembershipViewId Version { get; set; } = new(0);
        public long NextGeneration { get; set; } = 1;
        public Dictionary<NodeReference, MembershipTableEntry> Entries { get; } = [];
    }
}
