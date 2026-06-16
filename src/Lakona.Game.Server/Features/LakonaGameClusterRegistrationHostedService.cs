using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Features;

public sealed class LakonaGameClusterRegistrationHostedService : IHostedService
{
    private const string ClusterName = "local";

    private readonly IServiceProvider _services;

    public LakonaGameClusterRegistrationHostedService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = _services.GetService<INodeDirectory>();
        var options = _services.GetService<ClusterOptions>();
        var catalog = _services.GetService<LakonaGameFeatureCatalog>();
        if (directory is null || options is null || catalog is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var registration = new NodeRegistration(
            ClusterName,
            new NodeId(options.NodeId),
            CreateEndpoints(options.AdvertisedEndpoints),
            CreateFeatures(catalog),
            now.AddSeconds(options.RouteLeaseSeconds),
            NodeState.Ready);
        var result = await directory.RegisterAsync(registration, now, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != NodeRegistrationStatus.Registered)
        {
            throw new InvalidOperationException(
                $"Lakona.Game cluster node registration failed with status '{result.Status}'.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, NodeEndpoint> CreateEndpoints(
        IReadOnlyDictionary<string, string> endpoints)
    {
        var result = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            result[endpoint.Key] = new NodeEndpoint(endpoint.Value);
        }

        return result;
    }

    private static IReadOnlyList<NodeFeatureDescriptor> CreateFeatures(
        LakonaGameFeatureCatalog catalog)
    {
        var result = new List<NodeFeatureDescriptor>();
        for (var i = 0; i < catalog.ActiveDefinitions.Count; i++)
        {
            var feature = i < catalog.ActiveFeatures.Count
                ? catalog.ActiveFeatures[i]
                : null;
            if (feature?.Discoverable == false)
            {
                continue;
            }

            result.Add(new NodeFeatureDescriptor(
                catalog.ActiveDefinitions[i].Name,
                feature?.Metadata));
        }

        return result;
    }
}
