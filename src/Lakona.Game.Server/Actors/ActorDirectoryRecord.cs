using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorDirectoryRecord
{
    public ActorDirectoryRecord(
        ActorId actorId,
        NodeId node,
        DateTimeOffset updatedAt)
    {
        ActorId = actorId;
        Node = node;
        UpdatedAt = updatedAt;
    }

    public ActorDirectoryRecord(
        ActorId actorId,
        NodeReference owner,
        ActorActivationId activationId,
        DateTimeOffset updatedAt)
        : this(actorId, owner.Node, updatedAt)
    {
        OwnerReference = owner ?? throw new ArgumentNullException(nameof(owner));
        ActivationId = activationId;
    }

    public ActorId ActorId { get; }

    public NodeId Node { get; }

    public NodeReference? OwnerReference { get; }

    public ActorActivationId? ActivationId { get; }

    public DateTimeOffset UpdatedAt { get; }
}
