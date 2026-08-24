namespace Lakona.Game.Server.Hosting;

internal sealed class ClusterMembershipDescriptorRefresher(
    MembershipTableHostedService membership)
    : IClusterNodeDescriptorRefresher
{
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        return membership.RefreshDescriptorAsync(cancellationToken);
    }

    public ValueTask MarkUnavailableAsync()
    {
        return membership.MarkUnavailableAsync();
    }
}
