namespace Lakona.Game.Server.Sessions;

internal sealed class GameConnectionDeliveryPolicyRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, DeliveryPolicy> policies = new(StringComparer.Ordinal);

    public void Set(string connectionId, bool reliablePush)
    {
        Set(connectionId, reliablePush, "legacy");
    }

    public void Set(string connectionId, bool reliablePush, string endpointScope, string? serializerProfile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointScope);
        lock (gate)
        {
            policies[connectionId] = new DeliveryPolicy(reliablePush, endpointScope, serializerProfile);
        }
    }

    public DeliveryPolicy GetBindingPolicy(string connectionId)
    {
        lock (gate)
        {
            return policies.TryGetValue(connectionId, out var policy)
                ? policy
                : DeliveryPolicy.Legacy;
        }
    }

    public void Remove(string connectionId)
    {
        lock (gate)
        {
            policies.Remove(connectionId);
        }
    }

    internal sealed record DeliveryPolicy(bool ReliablePush, string EndpointScope, string? SerializerProfile)
    {
        internal static readonly DeliveryPolicy Legacy = new(false, "legacy", null);
    }
}
