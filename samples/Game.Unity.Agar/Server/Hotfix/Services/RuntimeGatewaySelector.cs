using Agar.Sample.State.Contracts;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Services;

public sealed class RuntimeGatewaySelector
{
    private static readonly FeatureName BattleRuntimeFeature = new("battle-runtime");

    private readonly IClusterNodeDiscovery? _discovery;
    private readonly IConfiguration _configuration;
    private readonly LakonaGameRuntimeOptions? _runtimeOptions;

    public RuntimeGatewaySelector(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _configuration = services.GetService<IConfiguration>()
            ?? new ConfigurationBuilder().Build();
        _discovery = services.GetService<IClusterNodeDiscovery>();
        _runtimeOptions = services.GetService<LakonaGameRuntimeOptions>();
    }

    public async ValueTask<GatewayEndpointDescriptor?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (_discovery is not null)
        {
            var node = await _discovery.AnyAsync(BattleRuntimeFeature, cancellationToken).ConfigureAwait(false);
            if (node is not null && node.Endpoints.TryGetValue("kcp", out var endpoint))
            {
                return EndpointDescriptorMapper.FromClusterEndpoint(node.Node, "kcp", endpoint);
            }
        }

        var localKcp = _runtimeOptions?.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Transport, "kcp", StringComparison.OrdinalIgnoreCase));
        if (localKcp is not null)
        {
            return EndpointDescriptorMapper.FromConfiguredEndpoint(_configuration, localKcp);
        }

        return null;
    }

    public bool IsLocalOwner(GatewayEndpointDescriptor? gateway)
    {
        var localId = EndpointDescriptorMapper.ResolveNodeId(_configuration);
        return gateway is not null
            && !string.IsNullOrWhiteSpace(gateway.InstanceId)
            && string.Equals(gateway.InstanceId, localId, StringComparison.Ordinal);
    }
}
