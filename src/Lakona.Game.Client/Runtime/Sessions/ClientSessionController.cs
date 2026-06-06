using System;
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;

namespace Lakona.Game.Client.Sessions
{
    public sealed class ClientSessionController
    {
        private readonly ReliablePushInbox _reliablePushInbox;

        public ClientSessionController(ReliablePushInbox? reliablePushInbox = null)
        {
            _reliablePushInbox = reliablePushInbox ?? new ReliablePushInbox();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.SignedOut, null, 0);
        }

        public ClientSessionSnapshot Snapshot { get; private set; }

        public void MarkConnecting()
        {
            if (!IsTerminalPhase(Snapshot.Phase))
            {
                SetPhase(ClientSessionPhase.Connecting);
            }
        }

        public void StartSession(GameSessionKey session, long lastReliableSequence = 0)
        {
            _reliablePushInbox.StartSession(session, lastReliableSequence);
            Snapshot = new ClientSessionSnapshot(
                ClientSessionPhase.Active,
                session,
                _reliablePushInbox.LastAppliedSequence);
        }

        public void MarkReconnecting()
        {
            if (!IsTerminalPhase(Snapshot.Phase))
            {
                SetPhase(ClientSessionPhase.Reconnecting);
            }
        }

        public void ApplyAckOutcome(ReliablePushAckOutcome outcome)
        {
            if (IsTerminalPhase(Snapshot.Phase))
            {
                return;
            }

            switch (outcome.Status)
            {
                case ReliablePushAckStatus.Accepted:
                case ReliablePushAckStatus.Duplicate:
                    break;
                case ReliablePushAckStatus.StateRefreshRequired:
                    SetPhase(ClientSessionPhase.RefreshRequired);
                    break;
                case ReliablePushAckStatus.StateLost:
                case ReliablePushAckStatus.SessionMismatch:
                    MarkStateLost();
                    break;
            }
        }

        public void ApplySessionTerminationNotice(SessionTerminationNotice notice)
        {
            if (notice is null)
            {
                throw new ArgumentNullException(nameof(notice));
            }

            if (Snapshot.Session is not { } session ||
                !session.Equals(notice.Session))
            {
                return;
            }

            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.Terminated, null, 0, notice);
        }

        public void MarkStateLost()
        {
            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.StateLost, null, 0);
        }

        public void EndSession()
        {
            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.SignedOut, null, 0);
        }

        private void SetPhase(ClientSessionPhase phase)
        {
            Snapshot = new ClientSessionSnapshot(
                phase,
                Snapshot.Session,
                _reliablePushInbox.LastAppliedSequence,
                Snapshot.Termination);
        }

        private static bool IsTerminalPhase(ClientSessionPhase phase)
        {
            return phase is ClientSessionPhase.StateLost or ClientSessionPhase.Terminated;
        }
    }
}
