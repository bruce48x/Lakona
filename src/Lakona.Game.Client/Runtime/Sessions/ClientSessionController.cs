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

        public void MarkReady()
        {
            if (Snapshot.Phase == ClientSessionPhase.Connecting)
            {
                SetPhase(ClientSessionPhase.Ready);
            }
        }

        public void MarkConnectionFailed(ClientConnectionFailure failure)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(
                ClientSessionPhase.ConnectionFailed,
                null,
                0,
                null,
                failure);
        }

        public void StartSession(string sessionId, long lastReliableSequence = 0)
        {
            StartSessionWithGeneration(sessionId, sessionGeneration: 1, lastReliableSequence);
        }

        public void StartSessionWithGeneration(
            string sessionId,
            long sessionGeneration,
            long lastReliableSequence = 0)
        {
            if (Snapshot.Phase == ClientSessionPhase.ConnectionFailed)
            {
                return;
            }

            if (sessionGeneration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionGeneration), "Session generation must be positive.");
            }

            _reliablePushInbox.StartSession(sessionId, sessionGeneration, lastReliableSequence);
            Snapshot = new ClientSessionSnapshot(
                ClientSessionPhase.Active,
                sessionId,
                _reliablePushInbox.LastAppliedSequence,
                null,
                null,
                sessionGeneration);
        }

        public void MarkReconnecting()
        {
            if (!IsTerminalPhase(Snapshot.Phase))
            {
                SetPhase(ClientSessionPhase.Reconnecting);
            }
        }

        public void MarkRecovered()
        {
            if (Snapshot.Phase == ClientSessionPhase.Reconnecting && Snapshot.SessionId is not null)
            {
                SetPhase(ClientSessionPhase.Active);
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
                    SetPhase(Snapshot.Phase);
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

            if (Snapshot.SessionId is null)
            {
                return;
            }

            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.Terminated, null, 0, notice, null);
        }

        public void MarkStateLost()
        {
            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.StateLost, null, 0, null, null);
        }

        public void EndSession()
        {
            _reliablePushInbox.Reset();
            Snapshot = new ClientSessionSnapshot(ClientSessionPhase.SignedOut, null, 0, null, null);
        }

        private void SetPhase(ClientSessionPhase phase)
        {
            Snapshot = new ClientSessionSnapshot(
                phase,
                Snapshot.SessionId,
                _reliablePushInbox.LastAppliedSequence,
                Snapshot.Termination,
                null,
                Snapshot.SessionGeneration);
        }

        private static bool IsTerminalPhase(ClientSessionPhase phase)
        {
            return phase is ClientSessionPhase.StateLost
                or ClientSessionPhase.Terminated
                or ClientSessionPhase.ConnectionFailed;
        }
    }
}
