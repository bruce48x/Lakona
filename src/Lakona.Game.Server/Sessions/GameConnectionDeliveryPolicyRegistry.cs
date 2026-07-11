namespace Lakona.Game.Server.Sessions;

internal sealed class GameConnectionDeliveryPolicyRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, bool> policies = new(StringComparer.Ordinal);

    public void Set(string connectionId, bool reliablePush)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        lock (gate)
        {
            policies[connectionId] = reliablePush;
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
        }
    }
}
