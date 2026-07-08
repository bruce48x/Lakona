using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed record ActorPlacementResult(
    ActorId ActorId,
    NodeId Owner);
