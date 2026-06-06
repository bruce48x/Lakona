namespace Lakona.Game.Abstractions
{
    public enum SessionResumeStatus
    {
        Resumed,
        StateRefreshRequired,
        StateLost,
        Unauthorized,
        Terminated
    }
}
