using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class InMemoryActorDirectoryCache : IActorDirectoryCache
{
    private readonly object _gate = new();
    private readonly Dictionary<ActorId, NodeId> _nodes = new();

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
        }
    }

    public void Remove(ActorId actorId)
    {
        lock (_gate)
        {
            _nodes.Remove(actorId);
        }
    }
}
