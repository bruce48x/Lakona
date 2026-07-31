using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;

namespace Lakona.Game.Server.Hosting;

public sealed class LocalClusterNodeDiscovery(
    LakonaGameRuntimeOptions runtimeOptions,
    ActorHostDescriptorCatalog actorHostCatalog,
    StartupActorDescriptorCatalog startupActorCatalog,
    IHotfixManager? hotfixManager = null,
    ClusterOptions? configuredClusterOptions = null) : IClusterNodeDiscovery
{
    public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> QueryAsync(
        ClusterNodeDiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var local = CreateDescriptor();
        IReadOnlyList<ClusterNodeDescriptor> result = query.Matches(local)
            ? [local]
            : [];
        return new ValueTask<IReadOnlyList<ClusterNodeDescriptor>>(result);
    }

    public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
        IReadOnlyDictionary<string, string> labels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return QueryAsync(new ClusterNodeDiscoveryQuery(labels: labels), cancellationToken);
    }

    public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
        IReadOnlyDictionary<string, string> labels,
        CancellationToken cancellationToken = default)
    {
        var nodes = await ListAsync(labels, cancellationToken).ConfigureAwait(false);
        return nodes.Count == 0 ? null : nodes[0];
    }

    private ClusterNodeDescriptor CreateDescriptor()
    {
        var clusterOptions = configuredClusterOptions ?? runtimeOptions.ToClusterOptions();
        var endpoints = clusterOptions.AdvertisedEndpoints.ToDictionary(
            static pair => pair.Key,
            static pair => new NodeEndpoint(pair.Value),
            StringComparer.Ordinal);
        var hotfixHosts = hotfixManager?.Current.ActorHosts.ToDictionary(
            static descriptor => descriptor.Actor,
            StringComparer.OrdinalIgnoreCase);
        var actorHosts = new List<NodeActorHostDescriptor>(runtimeOptions.ActorHosts.Count);
        foreach (var actor in runtimeOptions.ActorHosts)
        {
            if (actorHostCatalog.TryGet(actor, out var descriptor))
            {
                actorHosts.Add(new NodeActorHostDescriptor(
                    descriptor.Actor,
                    descriptor.PolicyHash,
                    descriptor.BuildTag,
                    descriptor.Metadata));
                continue;
            }

            if (hotfixHosts is null || !hotfixHosts.TryGetValue(actor, out var hotfix))
            {
                throw new InvalidOperationException(
                    $"Lakona:ActorHosts contains unknown actor host '{actor}'.");
            }

            actorHosts.Add(new NodeActorHostDescriptor(
                hotfix.Actor,
                hotfix.PolicyHash,
                hotfix.BuildTag,
                hotfix.Metadata));
        }

        return new ClusterNodeDescriptor(
            new NodeId(clusterOptions.NodeId),
            NodeState.Ready,
            endpoints,
            actorHosts.OrderBy(static host => host.Actor, StringComparer.OrdinalIgnoreCase).ToArray(),
            startupActorCatalog.Snapshot());
    }
}
