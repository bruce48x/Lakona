namespace Lakona.Game.Server.Sessions;

internal sealed class GameConnectionDeliveryPolicyRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, bool> policies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> endpointScopes = new(StringComparer.Ordinal);

    public void Set(string connectionId, bool reliablePush)
    {
        Set(connectionId, reliablePush, "legacy");
    }

    public void Set(string connectionId, bool reliablePush, string endpointScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointScope);
        lock (gate)
        {
            policies[connectionId] = reliablePush;
            endpointScopes[connectionId] = endpointScope;
        }
    }

    public string GetEndpointScope(string connectionId)
    {
        lock (gate)
        {
            return endpointScopes.TryGetValue(connectionId, out var endpointScope)
                ? endpointScope
                : "legacy";
        }
    }

    public bool Get(string connectionId)
    {
        lock (gate)
        {
            return policies.TryGetValue(connectionId, out var reliablePush) && reliablePush;
        }
    }

    public void Remove(string connectionId)
    {
        lock (gate)
        {
            policies.Remove(connectionId);
            endpointScopes.Remove(connectionId);
        }
    }
}
