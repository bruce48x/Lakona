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
    private readonly object generationGate = new();
    private Task<MembershipTableGeneration>? generationTask;
    private bool joinCommitted;
    private ClusterMembershipFencedException? joinFailure;
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

    // Allocation is attempted once, including failures with an unknown commit outcome.
    // The host calls this outside its retry loop; direct JoinAsync callers share it.
    internal ValueTask<MembershipTableGeneration> PrepareJoinAsync(CancellationToken cancellationToken)
    {
        lock (generationGate)
        {
            generationTask ??= AllocateOnceAsync(cancellationToken);
            return new ValueTask<MembershipTableGeneration>(generationTask);
        }
    }

    private async Task<MembershipTableGeneration> AllocateOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var generation = await table.AllocateGenerationAsync(buildTag.Value, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return generation;
    }

    public async ValueTask<NodeReference> JoinAsync(CancellationToken cancellationToken = default)
    {
        if (local is not null) throw new InvalidOperationException("The local node has already joined membership.");
        if (joinFailure is not null) throw joinFailure;
        try
        {
            return await JoinCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ClusterMembershipFencedException exception)
        {
            joinFailure = exception;
            throw;
        }
    }

    private async ValueTask<NodeReference> JoinCoreAsync(CancellationToken cancellationToken)
    {
        var generation = await PrepareJoinAsync(cancellationToken).ConfigureAwait(false);
        var reference = new NodeReference(generation.Cluster, nodeId, nodeIncarnation);
        var conflicts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadExactAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Cluster != generation.Cluster)
            {
                throw new ClusterMembershipFencedException("The membership cluster incarnation changed while this node was joining.");
            }

            var own = snapshot.Entries.SingleOrDefault(entry => entry.Reference == reference);
            if (own is not null)
            {
                if (own.Status != MembershipTableStatus.Joining || own.Generation != generation.Value)
                {
                    throw new ClusterMembershipFencedException("The joining node incarnation is no longer valid in the membership table.");
                }

                membership.Initialize(new MembershipTableSnapshotProjection(snapshot));
                local = reference;
                return reference;
            }

            if (joinCommitted)
            {
                throw new ClusterMembershipFencedException("The committed joining node incarnation is missing from the membership table.");
            }

            if (snapshot.Entries.Any(entry => entry.Reference.Node == nodeId && entry.Generation >= generation.Value))
            {
                throw new ClusterMembershipFencedException($"Node id '{nodeId.Value}' already has a newer incarnation.");
            }

            var existing = snapshot.Entries.SingleOrDefault(entry =>
                entry.Reference.Node == nodeId && entry.Status != MembershipTableStatus.Dead);
            var membersAfterReplacement = snapshot.Entries.Count(static entry =>
                entry.Status is MembershipTableStatus.Joining or MembershipTableStatus.Active);
            if (existing?.Status is MembershipTableStatus.Joining or MembershipTableStatus.Active)
                membersAfterReplacement--;
            if (membersAfterReplacement >= ClusterMembershipSnapshot.MaximumMembersV1)
            {
                throw new ClusterMembershipFencedException(
                    $"Node '{nodeId.Value}' cannot join: Membership v1 capacity of {ClusterMembershipSnapshot.MaximumMembersV1} members is full. "
                    + "Free a member slot before restarting this process.");
            }
            // Commit against the same view used for capacity accounting. A losing
            // CAS rereads and rechecks; confirming an existing own row above is free.
            var joining = new MembershipTableEntry(
                reference,
                MembershipTableStatus.Joining,
                endpoint,
                version: 1,
                iAmAliveTime: startedAt,
                startTime: startedAt,
                generation: generation.Value);
            joinCommitted = existing is null
                ? await table.TryInsertAsync(joining, snapshot.Version, cancellationToken).ConfigureAwait(false)
                : await table.TryReplaceAsync(existing.Reference, existing.Version, joining, snapshot.Version, cancellationToken).ConfigureAwait(false);
            if (!joinCommitted)
            {
                await DelayAfterConflictAsync(++conflicts, cancellationToken).ConfigureAwait(false);
            }
            // Confirm through the same checks, including retries after a lost response.
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
