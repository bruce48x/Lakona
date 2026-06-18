using Agar.Sample.State.Contracts;

namespace Server.App.Services;

internal sealed class GatewayNodeIdentity
{
    public GatewayNodeIdentity(GatewayEndpointDescriptor advertisedEndpoint)
    {
        AdvertisedEndpoint = advertisedEndpoint ?? throw new ArgumentNullException(nameof(advertisedEndpoint));
        InstanceId = string.IsNullOrWhiteSpace(advertisedEndpoint.InstanceId)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : advertisedEndpoint.InstanceId;
    }

    public string InstanceId { get; }

    public GatewayEndpointDescriptor AdvertisedEndpoint { get; }

    public bool IsRuntimeOwner(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null
            && !string.IsNullOrWhiteSpace(gateway.InstanceId)
            && string.Equals(gateway.InstanceId, InstanceId, StringComparison.Ordinal);
    }
}
