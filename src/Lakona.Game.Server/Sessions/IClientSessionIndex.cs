namespace Lakona.Game.Server.Sessions;

public interface IClientSessionIndex
{
    ValueTask UpdateAsync(
        string userId,
        string sessionKind,
        GameSessionKey session,
        long generation,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(
        string userId,
        string sessionKind,
        GameSessionKey session,
        long generation,
        CancellationToken cancellationToken = default);

    ValueTask<ClientSessionIndexEntry?> FindCurrentAsync(
        string userId,
        string sessionKind,
        CancellationToken cancellationToken = default);
}

public sealed record ClientSessionIndexEntry(
    string UserId,
    string SessionKind,
    GameSessionKey Session,
    long Generation);
