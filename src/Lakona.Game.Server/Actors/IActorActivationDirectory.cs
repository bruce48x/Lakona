using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public interface IActorActivationDirectory
{
    ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId actorId,
        NodeReference proposedOwner,
        ActorActivationId proposedActivation,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ReleaseAsync(
        ActorId actorId,
        ActorActivationId expectedActivation,
        CancellationToken cancellationToken = default);
}

public sealed record ActorActivationAcquireResult(
    ActorDirectoryRecord Record,
    bool Acquired);
