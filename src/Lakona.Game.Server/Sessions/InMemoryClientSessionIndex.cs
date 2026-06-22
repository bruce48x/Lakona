namespace Lakona.Game.Server.Sessions;

public sealed class InMemoryClientSessionIndex : IClientSessionIndex
{
    private readonly object _gate = new();
    private readonly Dictionary<(string UserId, string SessionKind), ClientSessionIndexEntry> _entries = [];

    public ValueTask UpdateAsync(
        string userId,
        string sessionKind,
        GameSessionKey session,
        long generation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKind);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entries[(userId, sessionKind)] = new ClientSessionIndexEntry(userId, sessionKind, session, generation);
        }

        return default;
    }

    public ValueTask RemoveAsync(
        string userId,
        string sessionKind,
        GameSessionKey session,
        long generation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKind);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (userId, sessionKind);
            if (_entries.TryGetValue(key, out var current) &&
                current.Session.Equals(session) &&
                current.Generation == generation)
            {
                _entries.Remove(key);
            }
        }

        return default;
    }

    public ValueTask<ClientSessionIndexEntry?> FindCurrentAsync(
        string userId,
        string sessionKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKind);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entries.TryGetValue((userId, sessionKind), out var entry);
            return new ValueTask<ClientSessionIndexEntry?>(entry);
        }
    }
}
