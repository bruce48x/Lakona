namespace Lakona.Game.Cluster.Membership;

internal interface IClusterMembershipRefresher
{
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}
