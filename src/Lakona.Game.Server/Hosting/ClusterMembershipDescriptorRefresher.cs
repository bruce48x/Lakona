namespace Lakona.Game.Server.Hosting;

internal sealed class ClusterMembershipDescriptorRefresher(
    ReplicatedClusterMembershipHostedService membership)
    : IClusterNodeDescriptorRefresher
{
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        return membership.RefreshDescriptorAsync(cancellationToken);
    }

    public ValueTask MarkUnavailableAsync(CancellationToken cancellationToken = default)
    {
        return membership.MarkUnavailableAsync(cancellationToken);
    }
}
