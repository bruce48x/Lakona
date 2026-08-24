using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorDirectoryRecord
{
    public ActorDirectoryRecord(
        ActorId actorId,
        NodeReference owner,
        ActorActivationId activationId,
        DateTimeOffset updatedAt)
    {
        ActorId = actorId;
        OwnerReference = owner ?? throw new ArgumentNullException(nameof(owner));
        ActivationId = activationId;
        UpdatedAt = updatedAt;
    }

    public ActorId ActorId { get; }

    public NodeId Node => OwnerReference.Node;

    public NodeReference OwnerReference { get; }

    public ActorActivationId ActivationId { get; }

    public DateTimeOffset UpdatedAt { get; }
}
