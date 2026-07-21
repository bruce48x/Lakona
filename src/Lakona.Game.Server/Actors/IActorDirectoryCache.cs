using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public interface IActorDirectoryCache
{
    bool TryGet(ActorId actorId, out NodeId node);

    void Set(ActorId actorId, NodeId node);

    bool TryGetRecord(ActorId actorId, out ActorDirectoryRecord? record)
    {
        record = null;
        return false;
    }

    void Set(ActorDirectoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Set(record.ActorId, record.Node);
    }

    void Remove(ActorId actorId);
}
