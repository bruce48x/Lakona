using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public interface IGameSessionConnectionCloser
{
    ValueTask CloseConnectionAsync(
        GameSessionKey session,
        string connectionId,
        SessionTerminationNotice notice,
        CancellationToken cancellationToken = default);
}
