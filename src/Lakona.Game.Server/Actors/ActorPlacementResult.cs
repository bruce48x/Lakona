using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed record ActorPlacementResult
{
    public ActorPlacementResult(ActorId actorId, NodeId owner)
    {
        ActorId = actorId;
        Owner = owner;
    }

    public ActorPlacementResult(ActorDirectoryRecord activation)
        : this(
            (activation ?? throw new ArgumentNullException(nameof(activation))).ActorId,
            activation.Node)
    {
        Activation = activation;
    }

    public ActorId ActorId { get; }

    public NodeId Owner { get; }

    public ActorDirectoryRecord? Activation { get; }
}
