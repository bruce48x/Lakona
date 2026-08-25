using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal interface IActorActivationSnapshotSource
{
    IReadOnlyList<ActorDirectoryRecord> CaptureRecoveryClaims();

    int ActiveCount { get; }
}
