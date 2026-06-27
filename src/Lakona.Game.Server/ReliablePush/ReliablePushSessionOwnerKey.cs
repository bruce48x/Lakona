using Lakona.Game.Server.Sessions;

using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.ReliablePush;

internal static class ReliablePushSessionOwnerKey
{
    public static string Create(GameSessionKey session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.Generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(session), "Session generation must be positive.");
        }

        return $"{session.OwnerKey}:{session.SessionId}:{session.Generation}";
    }
}
