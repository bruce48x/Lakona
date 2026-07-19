namespace Lakona.Game.Client.Sessions
{
    public readonly struct ClientSessionSnapshot
    {
        public ClientSessionSnapshot(
            ClientSessionPhase phase,
            string? sessionId,
            long lastReliableSequence,
            Lakona.Game.Abstractions.SessionTerminationNotice? termination = null,
            ClientConnectionFailure? failure = null)
        {
            Phase = phase;
            SessionId = sessionId;
            LastReliableSequence = lastReliableSequence;
            Termination = termination;
            Failure = failure;
        }

        public ClientSessionPhase Phase { get; }

        public string? SessionId { get; }

        public long LastReliableSequence { get; }

        public Lakona.Game.Abstractions.SessionTerminationNotice? Termination { get; }

        public ClientConnectionFailure? Failure { get; }
    }
}
