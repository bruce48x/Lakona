using System.Diagnostics;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Cluster.Actors;

internal enum ActorDirectoryOperationStatus
{
    Applied,
    ConditionFailed,
    RefreshRequired,
    Unavailable
}

internal sealed record ActorDirectoryOperationResult(
    ActorDirectoryOperationStatus Status,
    MembershipViewId View,
    ActorDirectoryRecord? Record);

internal sealed class ActorDirectoryPartition
{
    private readonly object gate = new();
    private readonly DistributedActorDirectory owner;
    private readonly Dictionary<ActorId, ActorDirectoryRecord> records = [];
    private readonly List<RangeLock> rangeLocks = [];
    private readonly List<RetainedSnapshot> retainedSnapshots = [];
    private readonly Dictionary<MembershipViewId, Task> snapshotReadiness = [];
    private Task transitionTail = Task.CompletedTask;
    private ActorDirectoryRing? currentRing;
    private ActorDirectoryRange currentRange = ActorDirectoryRange.Empty;
    private Exception? currentFailure;
    private MembershipViewId failureView;

    public ActorDirectoryPartition(
        ActorDirectoryPartitionId id,
        DistributedActorDirectory owner)
    {
        Id = id;
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public ActorDirectoryPartitionId Id { get; }

    public PreparedTransition PrepareTransition(
        ActorDirectoryRing? previous,
        ActorDirectoryRing current)
    {
        ArgumentNullException.ThrowIfNull(current);
        lock (gate)
        {
            var oldRange = previous?.GetRange(Id) ?? ActorDirectoryRange.Empty;
            var newRange = current.GetRange(Id);
            var removed = oldRange.Difference(newRange);
            var added = newRange.Difference(oldRange);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var predecessor = transitionTail;
            transitionTail = completion.Task;

            var transition = new PreparedTransition(
                this,
                predecessor,
                previous,
                current,
                removed,
                added,
                completion,
                releaseReady);
            if (previous is not null
                && current.View.Value == previous.View.Value + 1
                && removed.Count != 0)
                snapshotReadiness[previous.View] = releaseReady.Task;
            foreach (var range in removed)
                rangeLocks.Add(new RangeLock(range, current.View, completion.Task));
            foreach (var range in added)
                rangeLocks.Add(new RangeLock(range, current.View, completion.Task));

            currentRing = current;
            currentRange = newRange;
            PruneSnapshotPartners(current);
            return transition;
        }
    }

    public async ValueTask<ActorDirectoryOperationResult> LookupAsync(
        ActorId actorId,
        MembershipViewId requestView,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var ring = await WaitForOwnershipAsync(actorId, requestView, cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
            {
                if (!CanCommitOperation(actorId, requestView, ring)) continue;
                ThrowIfFailed(ring);
                if (ring.GetOwner(actorId) != Id)
                    return new(ActorDirectoryOperationStatus.RefreshRequired, ring.View, null);
                records.TryGetValue(actorId, out var record);
                if (record is not null && !IsActiveHost(record, ring))
                {
                    records.Remove(actorId);
                    record = null;
                }

                return new(ActorDirectoryOperationStatus.Applied, ring.View, record);
            }
        }
    }

    public async ValueTask<ActorDirectoryOperationResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        MembershipViewId requestView,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var ring = await WaitForOwnershipAsync(actorId, requestView, cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
            {
                if (!CanCommitOperation(actorId, requestView, ring)) continue;
                ThrowIfFailed(ring);
                if (ring.GetOwner(actorId) != Id)
                    return new(ActorDirectoryOperationStatus.RefreshRequired, ring.View, null);
                if (!IsActiveMember(proposedOwner, ring))
                    return new(ActorDirectoryOperationStatus.RefreshRequired, ring.View, null);

                if (records.TryGetValue(actorId, out var existing) && IsActiveHost(existing, ring))
                    return new(ActorDirectoryOperationStatus.ConditionFailed, ring.View, existing);

                var record = new ActorDirectoryRecord(
                    actorId,
                    proposedOwner,
                    proposedActivation,
                    DateTimeOffset.UtcNow);
                records[actorId] = record;
                return new(ActorDirectoryOperationStatus.Applied, ring.View, record);
            }
        }
    }

    public async ValueTask<ActorDirectoryOperationResult> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        MembershipViewId requestView,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var ring = await WaitForOwnershipAsync(actorId, requestView, cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
            {
                if (!CanCommitOperation(actorId, requestView, ring)) continue;
                ThrowIfFailed(ring);
                if (ring.GetOwner(actorId) != Id)
                    return new(ActorDirectoryOperationStatus.RefreshRequired, ring.View, null);
                if (!records.TryGetValue(actorId, out var existing))
                    return new(ActorDirectoryOperationStatus.Applied, ring.View, null);
                if (existing.ActivationId != expectedActivation)
                    return new(ActorDirectoryOperationStatus.ConditionFailed, ring.View, existing);

                records.Remove(actorId);
                return new(ActorDirectoryOperationStatus.Applied, ring.View, null);
            }
        }
    }

    public async ValueTask<IReadOnlyList<ActorDirectoryRecord>?> GetSnapshotAsync(
        MembershipViewId requestView,
        MembershipViewId snapshotView,
        ActorDirectoryRange range,
        CancellationToken cancellationToken)
    {
        await owner.EnsureViewAsync(requestView, cancellationToken).ConfigureAwait(false);
        Task? wait;
        lock (gate)
        {
            var available = retainedSnapshots.FirstOrDefault(value => value.View == snapshotView);
            if (available is not null)
                return available.Records
                    .Where(record => range.Contains(record.ActorId))
                    .OrderBy(static record => record.ActorId.Value, StringComparer.Ordinal)
                    .ToArray();
            snapshotReadiness.TryGetValue(snapshotView, out wait);
        }

        if (wait is null) return null;
        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (gate)
        {
            var snapshot = retainedSnapshots.FirstOrDefault(value => value.View == snapshotView);
            if (snapshot is null) return null;
            return snapshot.Records
                .Where(record => range.Contains(record.ActorId))
                .OrderBy(static record => record.ActorId.Value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public void AcknowledgeSnapshot(
        MembershipViewId snapshotView,
        ActorDirectoryPartitionId receiver)
    {
        lock (gate)
        {
            for (var index = retainedSnapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = retainedSnapshots[index];
                if (snapshot.View != snapshotView) continue;
                snapshot.TransferPartners.Remove(receiver);
                if (snapshot.TransferPartners.Count == 0)
                {
                    retainedSnapshots.RemoveAt(index);
                    snapshotReadiness.Remove(snapshot.View);
                }
            }
        }
    }

    private async ValueTask<ActorDirectoryRing> WaitForOwnershipAsync(
        ActorId actorId,
        MembershipViewId requestView,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await owner.EnsureViewAsync(requestView, cancellationToken).ConfigureAwait(false);
            ActorDirectoryRing ring;
            Task[] waits;
            Exception? failure;
            var hash = ActorDirectoryRing.Hash(actorId);
            lock (gate)
            {
                ring = currentRing ?? throw new ActorDirectoryUnavailableException(
                    "Actor Directory has not observed Membership yet.");
                var waitView = ring.View.CompareTo(requestView) > 0 ? ring.View : requestView;
                waits = rangeLocks
                    .Where(value => value.View.CompareTo(waitView) <= 0 && value.Range.Contains(hash))
                    .Select(static value => value.Completion)
                    .Distinct()
                    .ToArray();
                failure = failureView == ring.View ? currentFailure : null;
            }

            if (waits.Length > 0)
            {
                await Task.WhenAll(waits).WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (failure is not null)
                throw new ActorDirectoryUnavailableException(
                    $"Actor Directory range is unavailable in Membership view '{ring.View.Value}'.",
                    failure);

            lock (gate)
            {
                if (ReferenceEquals(ring, currentRing)) return ring;
            }
        }
    }

    private async Task RunTransitionAsync(PreparedTransition transition)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ClusterDiagnostics.StartActivity("cluster.actor_directory.transition");
        try
        {
            try
            {
                await transition.Predecessor.ConfigureAwait(false);
            }
            catch
            {
                // A newer view is allowed to recover after a failed older view.
            }

            foreach (var range in transition.Removed)
                ReleaseRange(transition, range);
            transition.ReleaseReady.TrySetResult();

            foreach (var range in transition.Added)
            {
                IReadOnlyList<ActorDirectoryRecord> incoming;
                var contiguous = transition.Previous is not null
                    && transition.Current.View.Value == transition.Previous.View.Value + 1;
                if (contiguous)
                {
                    incoming = await owner.TransferRangeAsync(
                            transition.Previous!,
                            transition.Current,
                            range,
                            owner.StoppingToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    incoming = await owner.RecoverRangeAsync(
                            transition.Current,
                            range,
                            owner.StoppingToken)
                        .ConfigureAwait(false);
                }

                ApplyRange(transition.Current, range, incoming);
                await owner.AcknowledgeRangeAsync(
                        transition.Previous,
                        transition.Current,
                        Id,
                        range,
                        owner.StoppingToken)
                    .ConfigureAwait(false);
            }

            lock (gate)
            {
                if (currentRing?.View == transition.Current.View)
                {
                    currentFailure = null;
                    failureView = default;
                }
            }
            ClusterDiagnostics.RecordActorDirectoryTransition(
                "success",
                Stopwatch.GetElapsedTime(started));
        }
        catch (Exception exception)
        {
            ClusterDiagnostics.RecordActorDirectoryTransition(
                "failure",
                Stopwatch.GetElapsedTime(started));
            ClusterDiagnostics.RecordActorDirectoryFailure(ActorDirectoryFailureReason.Unavailable);
            lock (gate)
            {
                if (currentRing?.View == transition.Current.View)
                {
                    currentFailure = exception;
                    failureView = transition.Current.View;
                }
            }
        }
        finally
        {
            transition.ReleaseReady.TrySetResult();
            lock (gate)
            {
                rangeLocks.RemoveAll(value => ReferenceEquals(value.Completion, transition.Completion.Task));
            }

            transition.Completion.TrySetResult();
        }
    }

    private void ReleaseRange(PreparedTransition transition, ActorDirectoryRange range)
    {
        lock (gate)
        {
            var removed = records.Values.Where(record => range.Contains(record.ActorId)).ToArray();
            foreach (var record in removed) records.Remove(record.ActorId);

            if (transition.Previous is null
                || transition.Current.View.Value != transition.Previous.View.Value + 1)
                return;

            var partners = transition.Current.GetIntersectingPartitions(range)
                .Select(static value => value.Partition)
                .Where(partition => partition != Id)
                .ToHashSet();
            if (partners.Count == 0) return;

            var existing = retainedSnapshots.FirstOrDefault(value => value.View == transition.Previous.View);
            if (existing is null)
            {
                retainedSnapshots.Add(new RetainedSnapshot(
                    transition.Previous.View,
                    removed.ToList(),
                    partners));
            }
            else
            {
                foreach (var record in removed)
                {
                    if (existing.Records.All(value => value.ActorId != record.ActorId))
                        existing.Records.Add(record);
                }

                existing.TransferPartners.UnionWith(partners);
            }
        }
    }

    private void ApplyRange(
        ActorDirectoryRing ring,
        ActorDirectoryRange range,
        IReadOnlyList<ActorDirectoryRecord> incoming)
    {
        lock (gate)
        {
            foreach (var record in incoming)
            {
                if (!range.Contains(record.ActorId) || ring.GetOwner(record.ActorId) != Id) continue;
                if (records.TryGetValue(record.ActorId, out var existing)
                    && (existing.OwnerReference != record.OwnerReference
                        || existing.ActivationId != record.ActivationId))
                {
                    throw new ActorDirectoryUnavailableException(
                        $"Conflicting live activations were recovered for '{record.ActorId.Value}'.");
                }

                records[record.ActorId] = record;
            }
        }
    }

    private void PruneSnapshotPartners(ActorDirectoryRing ring)
    {
        var active = ring.Membership.Members
            .Where(static member => member.State == ClusterMemberState.Active)
            .Select(static member => member.Reference)
            .ToHashSet();
        for (var index = retainedSnapshots.Count - 1; index >= 0; index--)
        {
            if (retainedSnapshots[index].View.Value < ring.View.Value - 1)
            {
                snapshotReadiness.Remove(retainedSnapshots[index].View);
                retainedSnapshots.RemoveAt(index);
                continue;
            }

            retainedSnapshots[index].TransferPartners.RemoveWhere(partition => !active.Contains(partition.Owner));
            if (retainedSnapshots[index].TransferPartners.Count == 0)
            {
                snapshotReadiness.Remove(retainedSnapshots[index].View);
                retainedSnapshots.RemoveAt(index);
            }
        }

        foreach (var view in snapshotReadiness.Keys
                     .Where(view => view.Value < ring.View.Value - 1)
                     .ToArray())
            snapshotReadiness.Remove(view);
    }

    private static bool IsActiveHost(ActorDirectoryRecord record, ActorDirectoryRing ring) =>
        record.OwnerReference is { } owner && IsActiveMember(owner, ring);

    private bool CanCommitOperation(
        ActorId actorId,
        MembershipViewId requestView,
        ActorDirectoryRing observedRing)
    {
        if (!ReferenceEquals(observedRing, currentRing)) return false;
        var waitView = observedRing.View.CompareTo(requestView) > 0 ? observedRing.View : requestView;
        var hash = ActorDirectoryRing.Hash(actorId);
        return !rangeLocks.Any(value =>
            value.View.CompareTo(waitView) <= 0 && value.Range.Contains(hash));
    }

    private void ThrowIfFailed(ActorDirectoryRing ring)
    {
        if (failureView == ring.View && currentFailure is not null)
            throw new ActorDirectoryUnavailableException(
                $"Actor Directory range is unavailable in Membership view '{ring.View.Value}'.",
                currentFailure);
    }

    private static bool IsActiveMember(NodeReference reference, ActorDirectoryRing ring) =>
        ring.Membership.Members.Any(member =>
            member.State == ClusterMemberState.Active && member.Reference == reference);

    internal sealed class PreparedTransition
    {
        internal PreparedTransition(
            ActorDirectoryPartition partition,
            Task predecessor,
            ActorDirectoryRing? previous,
            ActorDirectoryRing current,
            IReadOnlyList<ActorDirectoryRange> removed,
            IReadOnlyList<ActorDirectoryRange> added,
            TaskCompletionSource completion,
            TaskCompletionSource releaseReady)
        {
            Partition = partition;
            Predecessor = predecessor;
            Previous = previous;
            Current = current;
            Removed = removed;
            Added = added;
            Completion = completion;
            ReleaseReady = releaseReady;
        }

        internal ActorDirectoryPartition Partition { get; }
        internal Task Predecessor { get; }
        internal ActorDirectoryRing? Previous { get; }
        internal ActorDirectoryRing Current { get; }
        internal IReadOnlyList<ActorDirectoryRange> Removed { get; }
        internal IReadOnlyList<ActorDirectoryRange> Added { get; }
        internal TaskCompletionSource Completion { get; }
        internal TaskCompletionSource ReleaseReady { get; }

        internal void Start() => _ = Partition.RunTransitionAsync(this);
    }

    private sealed record RangeLock(
        ActorDirectoryRange Range,
        MembershipViewId View,
        Task Completion);

    private sealed record RetainedSnapshot(
        MembershipViewId View,
        List<ActorDirectoryRecord> Records,
        HashSet<ActorDirectoryPartitionId> TransferPartners);
}
