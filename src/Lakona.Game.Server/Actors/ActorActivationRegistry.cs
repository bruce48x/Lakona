namespace Lakona.Game.Server.Actors;

internal sealed class ActorActivationRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> records = new();

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
}
