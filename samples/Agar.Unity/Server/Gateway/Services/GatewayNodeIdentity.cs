using Microsoft.Extensions.Configuration;
using Agar.Sample.State.Contracts;
using Shared.Interfaces;
using Lakona.Game.Server.Configuration;

namespace Gateway.Services;

internal sealed class GatewayNodeIdentity
{
    public GatewayNodeIdentity(IConfiguration configuration, ServerRpcServerOptions realtimeOptions)
    {
        InstanceId = configuration["Lakona.Game:Node:Id"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            InstanceId = $"{Environment.MachineName}-{Environment.ProcessId}";
        }

        RealtimeEndpoint = new GatewayEndpointDescriptor
        {
            InstanceId = InstanceId,
            Transport = RealtimeTransportToString(realtimeOptions.Transport),
            Host = realtimeOptions.Host,
            Port = realtimeOptions.Port,
            Path = realtimeOptions.Path
        };
    }

    public string InstanceId { get; }

    public GatewayEndpointDescriptor RealtimeEndpoint { get; }

    public bool IsRuntimeOwner(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null
            && !string.IsNullOrWhiteSpace(gateway.InstanceId)
            && string.Equals(gateway.InstanceId, InstanceId, StringComparison.Ordinal);
    }

    private static string RealtimeTransportToString(string transport) =>
        string.IsNullOrWhiteSpace(transport) ? "unknown" : transport.ToLowerInvariant();
}
