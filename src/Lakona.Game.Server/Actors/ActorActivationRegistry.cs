using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorActivationRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> records = new();
    private long observedMembershipView;
    private bool reachedReady;

    public void Observe(ClusterMembershipSnapshot snapshot, NodeId localNode)
    {
        lock (gate)
        {
            if (snapshot.View.Value > observedMembershipView) observedMembershipView = snapshot.View.Value;
            reachedReady |= snapshot.Members.Any(member =>
                member.Reference.Node == localNode && member.State == ClusterMemberState.Ready);
        }
    }

    public void Set(ActorDirectoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        // Legacy/custom process-local directory adapters expose node-only
        // records. They never participate in distributed recovery.
        if (record.OwnerReference is null || record.ActivationId is null)
            return;
        lock (gate) records[record.ActorId] = record;
    }

    public void Remove(ActorId actorId, ActorActivationId activationId)
    {
        lock (gate)
        {
            if (records.TryGetValue(actorId, out var record) && record.ActivationId == activationId)
                records.Remove(actorId);
        }
    }

    public IReadOnlyList<ActorDirectoryRecord> SnapshotShard(int shard)
    {
        lock (gate)
            return records.Values.Where(record => ActorLocationLayout.GetShard(record.ActorId) == shard).ToArray();
    }

    public bool HasObserved(MembershipViewId view)
    {
        lock (gate) return observedMembershipView >= view.Value;
    }

    public bool HasReachedReady
    {
        get { lock (gate) return reachedReady; }
    }
}
