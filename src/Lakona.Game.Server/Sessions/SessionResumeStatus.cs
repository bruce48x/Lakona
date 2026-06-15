namespace Lakona.Game.Server.Sessions;

public enum SessionResumeStatus
{
    Resumed,
    StateRefreshRequired,
    StateLost,
    Unauthorized,
    Terminated
}
