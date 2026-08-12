using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class InMemoryActorDirectoryCache : IActorDirectoryCache
{
    private const int DefaultCapacity = 65536;
    private readonly object _gate = new();
    private readonly Dictionary<ActorId, NodeId> _nodes = new();
    private readonly Dictionary<ActorId, ActorDirectoryRecord> _records = new();
    private readonly Queue<ActorId> _clock = new();
    private readonly int _capacity;

    public InMemoryActorDirectoryCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

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
            _clock.Enqueue(actorId);
            if (_records.TryGetValue(actorId, out var record) && record.Node != node)
            {
                _records.Remove(actorId);
            }
            Trim();
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
            _clock.Enqueue(record.ActorId);
            Trim();
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

    private void Trim()
    {
        while (_nodes.Count > _capacity && _clock.TryDequeue(out var candidate))
        {
            _nodes.Remove(candidate);
            _records.Remove(candidate);
        }
        while (_clock.Count > _capacity * 4)
            _clock.Dequeue();
    }
}
