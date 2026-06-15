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

        public string? CurrentSessionId { get; private set; }

        public long LastAppliedSequence
        {
            get { return _tracker.LastAppliedSequence; }
        }

        public void StartSession(string sessionId, long lastAppliedSequence = 0)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session id is required.", nameof(sessionId));
            }

            CurrentSessionId = sessionId;
            _tracker.Reset();
            _tracker.MarkApplied(lastAppliedSequence);
        }

        public async ValueTask StartSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            var lastAppliedSequence = await _cursorStore.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            StartSession(sessionId, lastAppliedSequence);
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
            var sessionId = EnsureStarted();
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
                await _cursorStore.SaveAsync(sessionId, _tracker.LastAppliedSequence, cancellationToken).ConfigureAwait(false);
            }

            ReliablePushAckOutcome? acknowledgement = null;
            if (decision.ShouldAck)
            {
                acknowledgement = await acknowledgeAsync(
                    new ReliablePushAck(sessionId, sequence),
                    cancellationToken).ConfigureAwait(false);
            }

            return new ReliablePushProcessResult(decision, acknowledgement);
        }

        public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            var sessionId = CurrentSessionId;
            Reset();

            if (sessionId is not null)
            {
                await _cursorStore.ClearAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Reset()
        {
            CurrentSessionId = null;
            _tracker.Reset();
        }

        private string EnsureStarted()
        {
            if (CurrentSessionId is null)
            {
                throw new InvalidOperationException("Reliable push session has not started.");
            }

            return CurrentSessionId;
        }
    }
}
