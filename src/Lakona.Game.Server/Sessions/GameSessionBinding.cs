using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionBinding<TCallback>
    where TCallback : class
{
    public GameSessionBinding(
        GameSessionKey session,
        string connectionId,
        TCallback callback)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public GameSessionKey Session { get; }

    public string ConnectionId { get; }

    public TCallback Callback { get; }
}
