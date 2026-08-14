using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Cluster.Actors;

internal sealed class ActorLocationShard
{
    internal const int MaximumRecords = 4096;
    private readonly object gate = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> records = new();
    private NodeReference owner;
    private MembershipViewId observedView;

    public ActorLocationShard(NodeReference owner, MembershipViewId observedView)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.observedView = observedView;
    }

    public MembershipViewId ObservedView
    {
        get { lock (gate) return observedView; }
    }

    internal NodeReference Owner
    {
        get { lock (gate) return owner; }
    }

    public ActorLocationResult Lookup(
        ActorId actorId,
        NodeReference requestOwner,
        MembershipViewId requestView)
    {
        lock (gate)
        {
            if (requestOwner != owner)
            {
                return ActorLocationResult.Refresh(owner, observedView);
            }

            AdvanceView(requestView);
            records.TryGetValue(actorId, out var record);
            return ActorLocationResult.Read(record, owner, observedView);
        }
    }

    public ActorLocationResult Register(
        ActorId actorId,
        NodeReference activationOwner,
        ActorActivationId activationId,
        NodeReference requestOwner,
        MembershipViewId requestView)
    {
        lock (gate)
        {
            if (requestOwner != owner || activationOwner.Cluster != owner.Cluster)
            {
                return ActorLocationResult.Refresh(owner, observedView);
            }

            AdvanceView(requestView);
            if (records.TryGetValue(actorId, out var existing))
            {
                return ActorLocationResult.ConditionFailed(existing, owner, observedView);
            }

            if (records.Count >= MaximumRecords)
            {
                return ActorLocationResult.Unavailable(owner, observedView);
            }

            var record = new ActorDirectoryRecord(
                actorId,
                activationOwner,
                activationId,
                DateTimeOffset.UtcNow);
            records.Add(actorId, record);
            return ActorLocationResult.Applied(record, owner, observedView);
        }
    }

    public ActorLocationResult Unregister(
        ActorId actorId,
        ActorActivationId expectedActivation,
        MembershipViewId requestView)
    {
        lock (gate)
        {
            AdvanceView(requestView);
            if (!records.TryGetValue(actorId, out var existing))
            {
                return ActorLocationResult.Applied(null, owner, observedView);
            }

            if (existing.ActivationId != expectedActivation)
            {
                return ActorLocationResult.ConditionFailed(existing, owner, observedView);
            }

            records.Remove(actorId);
            return ActorLocationResult.Applied(null, owner, observedView);
        }
    }

    internal bool TryAdvanceStableOwner(NodeReference value, MembershipViewId view)
    {
        lock (gate)
        {
            if (owner != value) return false;
            // A skipped Membership view may hide an owner-away-and-back sequence.
            // Reacquire instead of trusting state whose intermediate owner history is unknown.
            if (view.Value > observedView.Value + 1) return false;
            AdvanceView(view);
            return true;
        }
    }

    internal void Restore(IReadOnlyList<ActorDirectoryRecord> recovered)
    {
        lock (gate)
        {
            if (recovered.Count > MaximumRecords)
            {
                throw new ActorDirectoryUnavailableException("Actor Location shard capacity is exhausted.");
            }
            foreach (var record in recovered)
            {
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

    internal void AdvanceRecoveredOwner(NodeReference value, MembershipViewId view)
    {
        lock (gate)
        {
            if (owner != value)
                throw new ActorDirectoryUnavailableException("Recovered Actor Location owner changed before publication.");
            AdvanceView(view);
        }
    }

    private void AdvanceView(MembershipViewId requestView)
    {
        if (requestView.CompareTo(observedView) > 0)
        {
            observedView = requestView;
        }
    }
}

internal enum ActorLocationMutationStatus
{
    Applied,
    ConditionFailed,
    RefreshRequired,
    Unavailable
}

internal sealed record ActorLocationResult(
    ActorLocationMutationStatus Status,
    ActorDirectoryRecord? Record,
    NodeReference Owner,
    MembershipViewId View)
{
    public static ActorLocationResult Applied(
        ActorDirectoryRecord? record,
        NodeReference owner,
        MembershipViewId view) => new(ActorLocationMutationStatus.Applied, record, owner, view);

    public static ActorLocationResult ConditionFailed(
        ActorDirectoryRecord record,
        NodeReference owner,
        MembershipViewId view) => new(ActorLocationMutationStatus.ConditionFailed, record, owner, view);

    public static ActorLocationResult Read(
        ActorDirectoryRecord? record,
        NodeReference owner,
        MembershipViewId view) => new(ActorLocationMutationStatus.Applied, record, owner, view);

    public static ActorLocationResult Refresh(
        NodeReference owner,
        MembershipViewId view) => new(ActorLocationMutationStatus.RefreshRequired, null, owner, view);

    public static ActorLocationResult Unavailable(
        NodeReference owner,
        MembershipViewId view) => new(ActorLocationMutationStatus.Unavailable, null, owner, view);
}
