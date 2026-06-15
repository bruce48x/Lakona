using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class NoopGameSessionConnectionCloser : IGameSessionConnectionCloser
{
    public ValueTask CloseConnectionAsync(
        GameSessionKey session,
        string connectionId,
        SessionTerminationNotice notice,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
