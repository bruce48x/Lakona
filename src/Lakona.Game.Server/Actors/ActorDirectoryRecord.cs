using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorDirectoryRecord
{
    public ActorDirectoryRecord(
        ActorId actorId,
        NodeId node,
        long version,
        DateTimeOffset updatedAt)
    {
        ActorId = actorId;
        Node = node;
        Version = version;
        UpdatedAt = updatedAt;
    }

    public ActorId ActorId { get; }

    public NodeId Node { get; }

    public long Version { get; }

    public DateTimeOffset UpdatedAt { get; }
}
