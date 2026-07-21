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

    public ActorDirectoryRecord(
        ActorId actorId,
        NodeReference owner,
        ActorActivationId activationId,
        long version,
        DateTimeOffset updatedAt)
        : this(actorId, owner.Node, version, updatedAt)
    {
        OwnerReference = owner ?? throw new ArgumentNullException(nameof(owner));
        ActivationId = activationId;
    }

    public ActorId ActorId { get; }

    public NodeId Node { get; }

    public NodeReference? OwnerReference { get; }

    public ActorActivationId? ActivationId { get; }

    public long Version { get; }

    public DateTimeOffset UpdatedAt { get; }
}
