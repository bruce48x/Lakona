using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class ReliablePushInbox
    {
        private readonly ReliablePushTracker _tracker = new ReliablePushTracker();
        private readonly IReliablePushCursorStore _cursorStore;

        public ReliablePushInbox(IReliablePushCursorStore? cursorStore = null)
        {
            _cursorStore = cursorStore ?? new InMemoryReliablePushCursorStore();
        }

        public GameSessionKey? CurrentSession { get; private set; }

        public long LastAppliedSequence
        {
            get { return _tracker.LastAppliedSequence; }
        }

        public void StartSession(GameSessionKey session, long lastAppliedSequence = 0)
        {
            CurrentSession = session;
            _tracker.Reset();
            _tracker.MarkApplied(lastAppliedSequence);
        }

        public async ValueTask StartSessionAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            var lastAppliedSequence = await _cursorStore.LoadAsync(session, cancellationToken).ConfigureAwait(false);
            StartSession(session, lastAppliedSequence);
        }

        public ReliablePushApplyDecision Decide(ReliablePushSequence sequence)
        {
            EnsureStarted();
            return _tracker.Decide(sequence.Value);
        }

        public async ValueTask<ReliablePushProcessResult> ProcessAsync<TPayload>(
            ReliablePushSequence sequence,
            TPayload payload,
            Func<TPayload, CancellationToken, ValueTask> applyAsync,
            Func<ReliablePushAck, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken = default)
        {
            var session = EnsureStarted();
            if (applyAsync is null)
            {
                throw new ArgumentNullException(nameof(applyAsync));
            }

            if (acknowledgeAsync is null)
            {
                throw new ArgumentNullException(nameof(acknowledgeAsync));
            }

            var decision = _tracker.Decide(sequence.Value);
            if (decision.ShouldApply)
            {
                await applyAsync(payload, cancellationToken).ConfigureAwait(false);
                _tracker.MarkApplied(sequence.Value);
                await _cursorStore.SaveAsync(session, _tracker.LastAppliedSequence, cancellationToken).ConfigureAwait(false);
            }

            ReliablePushAckOutcome? acknowledgement = null;
            if (decision.ShouldAck)
            {
                acknowledgement = await acknowledgeAsync(
                    new ReliablePushAck(session, sequence),
                    cancellationToken).ConfigureAwait(false);
            }

            return new ReliablePushProcessResult(decision, acknowledgement);
        }

        public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            var session = CurrentSession;
            Reset();

            if (session.HasValue)
            {
                await _cursorStore.ClearAsync(session.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Reset()
        {
            CurrentSession = null;
            _tracker.Reset();
        }

        private GameSessionKey EnsureStarted()
        {
            if (!CurrentSession.HasValue)
            {
                throw new InvalidOperationException("Reliable push session has not started.");
            }

            return CurrentSession.Value;
        }
    }
}
