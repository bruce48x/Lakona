using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;

namespace Lakona.Game.Client
{
    public sealed class LakonaGameClient
    {
        private readonly ClientSessionController _sessions;
        private readonly ReliablePushInbox _reliablePush;

        public LakonaGameClient(IReliablePushCursorStore? cursorStore = null)
        {
            _reliablePush = new ReliablePushInbox(cursorStore);
            _sessions = new ClientSessionController(_reliablePush);
        }

        public ClientSessionSnapshot Snapshot
        {
            get { return _sessions.Snapshot; }
        }

        public void MarkConnecting()
        {
            _sessions.MarkConnecting();
        }

        public void StartSession(GameSessionKey session, long lastReliableSequence = 0)
        {
            _sessions.StartSession(session, lastReliableSequence);
        }

        public async ValueTask StartSessionAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            await _reliablePush.StartSessionAsync(session, cancellationToken).ConfigureAwait(false);
            _sessions.StartSession(session, _reliablePush.LastAppliedSequence);
        }

        public void MarkReconnecting()
        {
            _sessions.MarkReconnecting();
        }

        public void ApplyAckOutcome(ReliablePushAckOutcome outcome)
        {
            _sessions.ApplyAckOutcome(outcome);
        }

        public void ApplySessionTerminationNotice(SessionTerminationNotice notice)
        {
            _sessions.ApplySessionTerminationNotice(notice);
        }

        public void MarkStateLost()
        {
            _sessions.MarkStateLost();
        }

        public void EndSession()
        {
            _sessions.EndSession();
        }

        public async ValueTask<ReliablePushProcessResult> ProcessReliablePushAsync<TPayload>(
            ReliablePushSequence sequence,
            TPayload payload,
            Func<TPayload, CancellationToken, ValueTask> applyAsync,
            Func<ReliablePushAck, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken = default)
        {
            var result = await _reliablePush
                .ProcessAsync(sequence, payload, applyAsync, acknowledgeAsync, cancellationToken)
                .ConfigureAwait(false);

            if (result.Acknowledgement.HasValue)
            {
                _sessions.ApplyAckOutcome(result.Acknowledgement.Value);
            }

            return result;
        }
    }
}
