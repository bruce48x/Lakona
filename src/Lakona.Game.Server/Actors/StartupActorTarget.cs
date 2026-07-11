using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed record StartupActorTarget(ActorId ActorId, NodeId Node, long NodeEpoch);
