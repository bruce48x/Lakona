using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class InMemoryActorDirectoryCache : IActorDirectoryCache
{
    private readonly object _gate = new();
    private readonly Dictionary<ActorId, NodeId> _nodes = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> _records = new();

    public bool TryGet(ActorId actorId, out NodeId node)
    {
        lock (_gate)
        {
            return _nodes.TryGetValue(actorId, out node);
        }
    }

    public void Set(ActorId actorId, NodeId node)
    {
        lock (_gate)
        {
            _nodes[actorId] = node;
            if (_records.TryGetValue(actorId, out var record) && record.Node != node)
            {
                _records.Remove(actorId);
            }
        }
    }

    public bool TryGetRecord(ActorId actorId, out ActorDirectoryRecord? record)
    {
        lock (_gate)
        {
            return _records.TryGetValue(actorId, out record);
        }
    }

    public void Set(ActorDirectoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _nodes[record.ActorId] = record.Node;
            _records[record.ActorId] = record;
        }
    }

    public void Remove(ActorId actorId)
    {
        lock (_gate)
        {
            _nodes.Remove(actorId);
            _records.Remove(actorId);
        }
    }
}
