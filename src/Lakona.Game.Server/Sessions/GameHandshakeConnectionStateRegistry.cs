using System.Collections.Concurrent;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameHandshakeConnectionStateRegistry
{
    private readonly ConcurrentDictionary<string, byte> completed = new(StringComparer.Ordinal);

    public bool IsComplete(string connectionId)
    {
        return completed.ContainsKey(connectionId);
    }

    public void MarkComplete(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));

        completed[connectionId] = 0;
    }

    public void Remove(string connectionId)
    {
        completed.TryRemove(connectionId, out _);
    }
}
