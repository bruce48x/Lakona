using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Hosting;

internal sealed class ClusterMembershipDescriptorRefresher(
    LakonaGameRuntimeOptions runtimeOptions,
    ReplicatedClusterMembershipHostedService membership)
    : IClusterNodeDescriptorRefresher
{
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        return IsReplicated
            ? membership.RefreshDescriptorAsync(cancellationToken)
            : ValueTask.CompletedTask;
    }

    public ValueTask MarkUnavailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    private bool IsReplicated =>
        runtimeOptions.Cluster.BootstrapNewCluster
        || runtimeOptions.Cluster.Seeds.Count > 0;
}
