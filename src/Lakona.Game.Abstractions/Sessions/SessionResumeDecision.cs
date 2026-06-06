namespace Lakona.Game.Abstractions
{
    public sealed class SessionResumeDecision
    {
        public SessionResumeDecision(
            SessionResumeStatus status,
            GameSessionKey? session,
            string? reason = null,
            SessionTerminationNotice? termination = null)
        {
            Status = status;
            Session = session;
            Reason = reason;
            Termination = termination;
        }

        public SessionResumeStatus Status { get; }

        public GameSessionKey? Session { get; }

        public string? Reason { get; }

        public SessionTerminationNotice? Termination { get; }

        public static SessionResumeDecision Resumed(GameSessionKey session)
        {
            return new SessionResumeDecision(SessionResumeStatus.Resumed, session);
        }

        public static SessionResumeDecision StateLost(string? reason = null)
        {
            return new SessionResumeDecision(SessionResumeStatus.StateLost, null, reason);
        }

        public static SessionResumeDecision StateRefreshRequired(
            GameSessionKey session,
            string? reason = null)
        {
            return new SessionResumeDecision(SessionResumeStatus.StateRefreshRequired, session, reason);
        }

        public static SessionResumeDecision Unauthorized(string? reason = null)
        {
            return new SessionResumeDecision(SessionResumeStatus.Unauthorized, null, reason);
        }

        public static SessionResumeDecision Terminated(SessionTerminationNotice notice)
        {
            if (notice is null)
            {
                throw new System.ArgumentNullException(nameof(notice));
            }

            return new SessionResumeDecision(
                SessionResumeStatus.Terminated,
                notice.Session,
                notice.Message,
                notice);
        }
    }
}
