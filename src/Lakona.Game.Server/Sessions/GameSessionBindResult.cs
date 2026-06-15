namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionBindResult
{
    public GameSessionBindResult(GameSessionSnapshot? sessionBecameActive)
    {
        SessionBecameActive = sessionBecameActive;
    }

    public GameSessionSnapshot? SessionBecameActive { get; }
}
