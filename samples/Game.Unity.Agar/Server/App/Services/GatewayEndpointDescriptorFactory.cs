using Agar.Sample.State.Contracts;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Configuration;

namespace Server.App.Services;

internal static class GatewayEndpointDescriptorFactory
{
    public static GatewayEndpointDescriptor FromConfiguredEndpoint(
        IConfiguration configuration,
        LakonaGameEndpointOptions endpoint)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(endpoint);

        return new GatewayEndpointDescriptor
        {
            InstanceId = ResolveNodeId(configuration),
            Transport = NormalizeTransport(endpoint.Transport),
            Host = string.IsNullOrWhiteSpace(endpoint.AdvertisedHost) ? endpoint.Host : endpoint.AdvertisedHost,
            Port = endpoint.Port,
            Path = string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path
        };
    }

    public static GatewayEndpointDescriptor FromClusterEndpoint(
        NodeId node,
        string transport,
        NodeEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var uri = new Uri(endpoint.Address, UriKind.Absolute);
        return new GatewayEndpointDescriptor
        {
            InstanceId = node.Value,
            Transport = NormalizeTransport(transport),
            Host = uri.Host,
            Port = uri.Port,
            Path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath
        };
    }

    public static string ResolveNodeId(IConfiguration configuration)
    {
        var id = configuration["Lakona:Node:Id"]
            ?? configuration["Lakona.Game:Node:Id"]
            ?? string.Empty;
        return string.IsNullOrWhiteSpace(id)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : id;
    }

    private static string NormalizeTransport(string transport)
    {
        return string.IsNullOrWhiteSpace(transport) ? "unknown" : transport.ToLowerInvariant();
    }
}
