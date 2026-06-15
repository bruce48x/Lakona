using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionSnapshot
{
    public GameSessionSnapshot(
        GameSessionKey session,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CallbackContractTypes = callbackContractTypes ?? throw new ArgumentNullException(nameof(callbackContractTypes));
    }

    public GameSessionKey Session { get; }

    public string ConnectionId { get; }

    public IReadOnlyList<Type> CallbackContractTypes { get; }
}
