using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal readonly record struct ActorLifecycleTarget(
    ActorId ActorId,
    NodeReference Owner,
    ActorActivationId ActivationId);

internal sealed record ActorHostCreateCommand(
    string Actor,
    ActorLifecycleTarget Target,
    ActorPlacementCreateMode Mode,
    string BuildTag);

internal sealed record ActorHostDestroyCommand(
    string Actor,
    ActorLifecycleTarget Target);

internal sealed record ActorHostCommandReply(
    bool Succeeded,
    NodeId? OwnerNode,
    string Message);
