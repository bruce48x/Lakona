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

        public long CurrentSessionGeneration { get; private set; }

        public long LastAppliedSequence
        {
            get { return _tracker.LastAppliedSequence; }
        }

        public void StartSession(string sessionId, long lastAppliedSequence = 0)
        {
            StartSession(sessionId, sessionGeneration: 1, lastAppliedSequence);
        }

        public void StartSession(
            string sessionId,
            long sessionGeneration,
            long lastAppliedSequence = 0)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session id is required.", nameof(sessionId));
            }

            if (sessionGeneration <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionGeneration),
                    "Session generation must be positive.");
            }

            CurrentSessionId = sessionId;
            CurrentSessionGeneration = sessionGeneration;
            _tracker.Reset();
            _tracker.MarkApplied(lastAppliedSequence);
        }

        public async ValueTask StartSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            await StartSessionAsync(sessionId, sessionGeneration: 1, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask StartSessionAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default)
        {
            var lastAppliedSequence = await _cursorStore
                .LoadAsync(sessionId, sessionGeneration, cancellationToken)
                .ConfigureAwait(false);
            StartSession(sessionId, sessionGeneration, lastAppliedSequence);
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
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
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
                await _cursorStore
                    .SaveAsync(
                        session.SessionId,
                        session.SessionGeneration,
                        _tracker.LastAppliedSequence,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            ReliablePushAckOutcome? acknowledgement = null;
            if (decision.ShouldAck)
            {
                acknowledgement = await acknowledgeAsync(
                    new ReliablePushAckRequest(session.SessionId, session.SessionGeneration, sequence),
                    cancellationToken).ConfigureAwait(false);
            }

            return new ReliablePushProcessResult(decision, acknowledgement);
        }

        public async ValueTask<ReliablePushProcessResult> ProcessAsync(
            ReliablePushMetadata metadata,
            Func<CancellationToken, ValueTask> applyAsync,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken = default)
        {
            var session = EnsureStarted();
            if (!StringComparer.Ordinal.Equals(session.SessionId, metadata.SessionId) ||
                session.SessionGeneration != metadata.SessionGeneration)
            {
                throw new InvalidOperationException("Reliable push metadata belongs to a different session.");
            }

            if (applyAsync is null)
            {
                throw new ArgumentNullException(nameof(applyAsync));
            }

            if (acknowledgeAsync is null)
            {
                throw new ArgumentNullException(nameof(acknowledgeAsync));
            }

            var decision = _tracker.Decide(metadata.Sequence.Value);
            if (decision.ShouldApply)
            {
                await applyAsync(cancellationToken).ConfigureAwait(false);
                _tracker.MarkApplied(metadata.Sequence.Value);
                await _cursorStore
                    .SaveAsync(
                        session.SessionId,
                        session.SessionGeneration,
                        _tracker.LastAppliedSequence,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            ReliablePushAckOutcome? acknowledgement = null;
            if (decision.ShouldAck)
            {
                acknowledgement = await acknowledgeAsync(
                    new ReliablePushAckRequest(metadata.SessionId, metadata.SessionGeneration, metadata.Sequence),
                    cancellationToken).ConfigureAwait(false);
            }

            return new ReliablePushProcessResult(decision, acknowledgement);
        }

        public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            var sessionId = CurrentSessionId;
            var sessionGeneration = CurrentSessionGeneration;
            Reset();

            if (sessionId is not null)
            {
                await _cursorStore
                    .ClearAsync(sessionId, sessionGeneration, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public void Reset()
        {
            CurrentSessionId = null;
            CurrentSessionGeneration = 0;
            _tracker.Reset();
        }

        private (string SessionId, long SessionGeneration) EnsureStarted()
        {
            if (CurrentSessionId is null)
            {
                throw new InvalidOperationException("Reliable push session has not started.");
            }

            return (CurrentSessionId, CurrentSessionGeneration);
        }
    }
}
