using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionSnapshot
{
    public GameSessionSnapshot(
        GameSessionKey session,
        string connectionId)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
    }

    public GameSessionKey Session { get; }

    public string ConnectionId { get; }

}
