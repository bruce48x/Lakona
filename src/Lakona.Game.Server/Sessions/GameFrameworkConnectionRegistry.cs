using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameFrameworkConnectionRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, RpcNotificationChannel> connections = new(StringComparer.Ordinal);

    public void Set(string connectionId, RpcNotificationChannel notifications)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));
        ArgumentNullException.ThrowIfNull(notifications);
        lock (gate)
        {
            connections[connectionId] = notifications;
        }
    }

    public RpcNotificationChannel? Get(string connectionId)
    {
        lock (gate)
        {
            return connections.TryGetValue(connectionId, out var connection) ? connection : null;
        }
    }

    public void Remove(string connectionId)
    {
        lock (gate)
        {
            connections.Remove(connectionId);
        }
    }
}
