using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal interface IActorLocationStabilizer
{
    void ObserveRecoveryView(ClusterMembershipSnapshot snapshot);

    ValueTask StabilizeAsync(
        ClusterMembershipSnapshot snapshot,
        int maximumConcurrency,
        CancellationToken cancellationToken);
}
