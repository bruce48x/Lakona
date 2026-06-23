using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionHeartbeatResult
{
    public GameSessionHeartbeatResult(
        GameSessionHeartbeatStatus status,
        GameSessionKey? session = null,
        SessionTerminationNotice? termination = null)
    {
        Status = status;
        Session = session;
        Termination = termination;
    }

    public GameSessionHeartbeatStatus Status { get; }

    public GameSessionKey? Session { get; }

    public SessionTerminationNotice? Termination { get; }

    public static GameSessionHeartbeatResult ConnectionOnly()
    {
        return new GameSessionHeartbeatResult(GameSessionHeartbeatStatus.ConnectionOnly);
    }

    public static GameSessionHeartbeatResult ActiveSession(GameSessionKey session)
    {
        return new GameSessionHeartbeatResult(GameSessionHeartbeatStatus.ActiveSession, session);
    }

    public static GameSessionHeartbeatResult StateLost()
    {
        return new GameSessionHeartbeatResult(GameSessionHeartbeatStatus.StateLost);
    }

    public static GameSessionHeartbeatResult Terminated(
        GameSessionKey session,
        SessionTerminationNotice termination)
    {
        ArgumentNullException.ThrowIfNull(termination);
        return new GameSessionHeartbeatResult(GameSessionHeartbeatStatus.Terminated, session, termination);
    }
}

public enum GameSessionHeartbeatStatus
{
    ConnectionOnly,
    ActiveSession,
    StateLost,
    Terminated
}
