namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionBindResult
{
    public GameSessionBindResult(GameSessionSnapshot? sessionBecameActive)
        : this(sessionBecameActive, false)
    {
    }

    public GameSessionBindResult(GameSessionSnapshot? sessionBecameActive, bool isFirstBinding)
    {
        SessionBecameActive = sessionBecameActive;
        IsFirstBinding = isFirstBinding;
    }

    public GameSessionSnapshot? SessionBecameActive { get; }

    /// <summary>True when this reservation will establish the session's first committed binding.</summary>
    public bool IsFirstBinding { get; }
}
