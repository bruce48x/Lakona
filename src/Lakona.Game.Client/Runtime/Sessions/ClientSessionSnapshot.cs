using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.Sessions
{
    public readonly struct ClientSessionSnapshot
    {
        public ClientSessionSnapshot(
            ClientSessionPhase phase,
            GameSessionKey? session,
            long lastReliableSequence,
            SessionTerminationNotice? termination = null)
        {
            Phase = phase;
            Session = session;
            LastReliableSequence = lastReliableSequence;
            Termination = termination;
        }

        public ClientSessionPhase Phase { get; }

        public GameSessionKey? Session { get; }

        public long LastReliableSequence { get; }

        public SessionTerminationNotice? Termination { get; }
    }
}
