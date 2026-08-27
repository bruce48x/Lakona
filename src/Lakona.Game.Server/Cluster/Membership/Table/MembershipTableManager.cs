namespace Lakona.Game.Cluster.Membership;

internal sealed class MembershipTableManager : IClusterMembershipRefresher
{
    private readonly NodeId nodeId;
    private readonly NodeIncarnationId nodeIncarnation;
    private readonly NodeEndpoint endpoint;
    private readonly ClusterBuildTag buildTag;
    private readonly IMembershipTable table;
    private readonly ClusterMembershipState membership;
    private readonly TimeProvider timeProvider;
    private readonly DateTimeOffset startedAt;
    private NodeReference? local;

    public MembershipTableManager(
        NodeId nodeId,
        NodeIncarnationId nodeIncarnation,
        NodeEndpoint endpoint,
        ClusterBuildTag buildTag,
        IMembershipTable table,
        ClusterMembershipState membership,
        TimeProvider? timeProvider = null)
    {
        this.nodeId = nodeId;
        this.nodeIncarnation = nodeIncarnation;
        this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        this.buildTag = buildTag ?? throw new ArgumentNullException(nameof(buildTag));
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        startedAt = this.timeProvider.GetUtcNow();
    }

    public NodeReference Local => local ?? throw new InvalidOperationException("The local node has not joined membership.");

    public async ValueTask<NodeReference> JoinAsync(CancellationToken cancellationToken = default)
    {
        if (local is not null) throw new InvalidOperationException("The local node has already joined membership.");
        var generation = await table.AllocateGenerationAsync(buildTag.Value, cancellationToken).ConfigureAwait(false);
        var conflicts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            var existing = snapshot.Entries.SingleOrDefault(entry =>
                entry.Reference.Node == nodeId && entry.Status != MembershipTableStatus.Dead);
            if (existing?.Reference.Incarnation == nodeIncarnation
                && existing.Status == MembershipTableStatus.Joining)
            {
                local = existing.Reference;
                membership.Initialize(new MembershipTableSnapshotProjection(snapshot));
                return existing.Reference;
            }

            if (existing is not null)
            {
                if (existing.Generation >= generation.Value)
                {
                    throw new ClusterMembershipFencedException(
                        $"Node id '{nodeId.Value}' already has a newer live incarnation.");
                }

                var replacement = new MembershipTableEntry(
                    new NodeReference(snapshot.Cluster, nodeId, nodeIncarnation),
                    MembershipTableStatus.Joining,
                    endpoint,
                    version: 1,
                    iAmAliveTime: startedAt,
                    startTime: startedAt,
                    generation: generation.Value);
                if (await table.TryReplaceAsync(
                    existing.Reference,
                    existing.Version,
                    replacement,
                    snapshot.Version,
                    cancellationToken).ConfigureAwait(false))
                {
                    var replacementSnapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
                    local = replacement.Reference;
                    membership.Initialize(new MembershipTableSnapshotProjection(replacementSnapshot));
                    return replacement.Reference;
                }

                await DelayAfterConflictAsync(++conflicts, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var reference = new NodeReference(snapshot.Cluster, nodeId, nodeIncarnation);
            if (snapshot.Cluster != generation.Cluster)
            {
                throw new ClusterMembershipFencedException("The membership cluster incarnation changed while this node was joining.");
            }
            var joining = new MembershipTableEntry(
                reference,
                MembershipTableStatus.Joining,
                endpoint,
                version: 1,
                iAmAliveTime: startedAt,
                startTime: startedAt,
                generation: generation.Value);
            if (!await table.TryInsertAsync(joining, snapshot.Version, cancellationToken).ConfigureAwait(false))
            {
                await DelayAfterConflictAsync(++conflicts, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var committed = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            local = reference;
            membership.Initialize(new MembershipTableSnapshotProjection(committed));
            return reference;
        }
    }

    public ValueTask ActivateAsync(
        IReadOnlyDictionary<string, string>? labels,
        IReadOnlyList<NodeActorHostDescriptor>? actorHosts,
        IReadOnlyList<StartupActorDescriptor>? startupActors,
        CancellationToken cancellationToken = default) =>
        UpdateLocalAsync(
            entry => entry.WithDescriptor(MembershipTableStatus.Active, endpoint, labels, actorHosts, startupActors),
            cancellationToken);

    public ValueTask MarkStoppingAsync(CancellationToken cancellationToken = default) =>
        UpdateLocalAsync(static entry => entry.WithStatus(MembershipTableStatus.Stopping), cancellationToken);

    public ValueTask MarkDeadAsync(CancellationToken cancellationToken = default) =>
        UpdateLocalAsync(static entry => entry.WithStatus(MembershipTableStatus.Dead), cancellationToken);

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
        var reference = Local;
        var entry = snapshot.Entries.SingleOrDefault(candidate => candidate.Reference == reference);
        if (entry is null || entry.Status == MembershipTableStatus.Dead)
        {
            throw new ClusterMembershipFencedException("The local node incarnation is dead or missing from the membership table.");
        }

        membership.Publish(new MembershipTableSnapshotProjection(snapshot));
    }

    public ValueTask<bool> UpdateIAmAliveAsync(CancellationToken cancellationToken = default) =>
        table.TryUpdateIAmAliveAsync(Local, timeProvider.GetUtcNow(), cancellationToken);

    public ValueTask<MembershipTableSnapshot> ReadTableAsync(CancellationToken cancellationToken = default) =>
        ReadExactAsync(cancellationToken);

    public ValueTask<int> CleanupDefunctAsync(
        TimeSpan retention,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        return table.CleanupDefunctAsync(
            timeProvider.GetUtcNow() - retention,
            maximumRows,
            cancellationToken);
    }

    public async ValueTask<bool> TryMarkDefunctAsync(
        NodeReference target,
        TimeSpan allowedIAmAliveMiss,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (allowedIAmAliveMiss <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(allowedIAmAliveMiss));
        if (target == Local) throw new ArgumentException("A node cannot declare itself defunct.", nameof(target));

        var conflicts = 0;
        while (true)
        {
            var snapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            var targetEntry = snapshot.Entries.SingleOrDefault(entry => entry.Reference == target);
            if (targetEntry?.Status != MembershipTableStatus.Active)
            {
                membership.Publish(new MembershipTableSnapshotProjection(snapshot));
                return true;
            }

            var now = timeProvider.GetUtcNow();
            if (now - targetEntry.IAmAliveTime <= allowedIAmAliveMiss)
            {
                membership.Publish(new MembershipTableSnapshotProjection(snapshot));
                return false;
            }

            var candidate = targetEntry.WithStatus(MembershipTableStatus.Dead);
            if (!await table.TryUpdateAsync(
                candidate,
                targetEntry.Version,
                snapshot.Version,
                cancellationToken).ConfigureAwait(false))
            {
                await DelayAfterConflictAsync(++conflicts, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var committed = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            membership.Publish(new MembershipTableSnapshotProjection(committed));
            return true;
        }
    }

    public async ValueTask<bool> TrySuspectAsync(
        NodeReference target,
        int configuredVotesForDeath,
        TimeSpan voteLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (configuredVotesForDeath <= 0) throw new ArgumentOutOfRangeException(nameof(configuredVotesForDeath));
        if (voteLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(voteLifetime));
        var observer = Local;
        if (target == observer) throw new ArgumentException("A node cannot suspect itself.", nameof(target));

        var conflicts = 0;
        while (true)
        {
            var snapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            var observerEntry = snapshot.Entries.SingleOrDefault(entry => entry.Reference == observer);
            var targetEntry = snapshot.Entries.SingleOrDefault(entry => entry.Reference == target);
            if (observerEntry?.Status != MembershipTableStatus.Active || targetEntry?.Status != MembershipTableStatus.Active)
            {
                return false;
            }

            var now = timeProvider.GetUtcNow();
            var activeObservers = snapshot.Entries
                .Where(entry => entry.Status == MembershipTableStatus.Active && entry.Reference != target)
                .ToDictionary(entry => entry.Reference.Node, entry => entry.Reference);
            var freshVotes = targetEntry.SuspectVotes
                .Where(vote => vote.Timestamp <= now
                    && now - vote.Timestamp <= voteLifetime
                    && activeObservers.TryGetValue(vote.Observer.Node, out var activeReference)
                    && activeReference == vote.Observer
                    && vote.Observer.Node != observer.Node)
                .ToList();
            freshVotes.Add(new MembershipSuspectVote(observer, now));
            var candidate = targetEntry.WithSuspectVotes(freshVotes);
            var freshVoteCount = freshVotes.Count;
            var activeCount = snapshot.Entries.Count(static entry => entry.Status == MembershipTableStatus.Active);
            var votesRequired = Math.Min(configuredVotesForDeath, (activeCount + 1) / 2);
            if (freshVoteCount >= votesRequired)
            {
                candidate = new MembershipTableEntry(
                    candidate.Reference,
                    MembershipTableStatus.Dead,
                    candidate.ClusterEndpoint,
                    candidate.Version,
                    candidate.IAmAliveTime,
                    candidate.Labels,
                    candidate.ActorHosts,
                    candidate.StartupActors,
                    candidate.SuspectVotes,
                    candidate.StartTime,
                    candidate.Generation);
            }

            if (!await table.TryUpdateAsync(candidate, targetEntry.Version, snapshot.Version, cancellationToken).ConfigureAwait(false))
            {
                await DelayAfterConflictAsync(++conflicts, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var committed = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            membership.Publish(new MembershipTableSnapshotProjection(committed));
            return candidate.Status == MembershipTableStatus.Dead;
        }
    }

    private async ValueTask UpdateLocalAsync(
        Func<MembershipTableEntry, MembershipTableEntry> update,
        CancellationToken cancellationToken)
    {
        var reference = Local;
        var conflicts = 0;
        while (true)
        {
            var snapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            var current = snapshot.Entries.SingleOrDefault(entry => entry.Reference == reference);
            if (current is null || current.Status == MembershipTableStatus.Dead)
            {
                throw new ClusterMembershipFencedException("The local node incarnation is dead or missing from the membership table.");
            }

            var next = update(current);
            if (!await table.TryUpdateAsync(next, current.Version, snapshot.Version, cancellationToken).ConfigureAwait(false))
            {
                await DelayAfterConflictAsync(++conflicts, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var committed = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            membership.Publish(new MembershipTableSnapshotProjection(committed));
            return;
        }
    }

    private Task DelayAfterConflictAsync(int conflictCount, CancellationToken cancellationToken)
    {
        var maximumDelayMilliseconds = Math.Min(1000, 10 << Math.Min(conflictCount, 7));
        var seed = HashCode.Combine(nodeIncarnation.Value, conflictCount) & int.MaxValue;
        var delay = TimeSpan.FromMilliseconds(1 + seed % maximumDelayMilliseconds);
        return Task.Delay(delay, timeProvider, cancellationToken);
    }

    private async ValueTask<MembershipTableSnapshot> ReadExactAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await table.ReadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(snapshot.BuildTag, buildTag.Value, StringComparison.Ordinal))
        {
            throw new ClusterMembershipFencedException(
                $"Node BuildTag '{buildTag.Value}' cannot use cluster BuildTag " +
                $"'{snapshot.BuildTag ?? "<uninitialized>"}'. Deploy incompatible BuildTags to separate environments.");
        }

        return snapshot;
    }
}

public sealed class ClusterMembershipFencedException(string message) : Exception(message);
