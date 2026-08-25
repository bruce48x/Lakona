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
    private readonly IClusterMembership? _membership;

    public InMemoryActorDirectoryCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public InMemoryActorDirectoryCache(IClusterMembership membership, int capacity = DefaultCapacity)
        : this(capacity)
    {
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
    }

    public bool TryGet(ActorId actorId, out NodeId node)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(actorId, out node)) return false;
            if (IsRoutable(actorId, node)) return true;
            RemoveLocked(actorId);
            node = default;
            return false;
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
            if (!_records.TryGetValue(actorId, out record)) return false;
            if (IsRoutable(record)) return true;
            RemoveLocked(actorId);
            record = null;
            return false;
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
            RemoveLocked(actorId);
        }
    }

    private bool IsRoutable(ActorId actorId, NodeId node)
    {
        if (_membership is null) return true;
        return _records.TryGetValue(actorId, out var record)
            ? IsRoutable(record)
            : _membership.Current.Members.Any(member =>
                member.State == ClusterMemberState.Active && member.Reference.Node == node);
    }

    private bool IsRoutable(ActorDirectoryRecord record)
    {
        if (_membership is null) return true;
        return record.OwnerReference is { } owner
            && _membership.Current.TryGetMember(owner, out var member)
            && member?.State == ClusterMemberState.Active;
    }

    private void RemoveLocked(ActorId actorId)
    {
        _nodes.Remove(actorId);
        _records.Remove(actorId);
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
