using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class ReliablePushInbox
    {
        private const int MaxSaveAttempts = 3;
        private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly ReliablePushTracker _tracker = new ReliablePushTracker();
        private readonly IReliablePushCursorStore _cursorStore;
        private readonly Action<ReliablePushCursorSaveException>? _onManualCursorSaveFailure;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _commitGate = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<bool> _ready = NewReady();
        private int _generation;
        private long _savedSequence;
        private ReliablePushCursorSaveException? _saveFailure;

        public ReliablePushInbox(IReliablePushCursorStore? cursorStore = null)
            : this(cursorStore, null) { }

        internal ReliablePushInbox(IReliablePushCursorStore? cursorStore, Action<ReliablePushCursorSaveException>? onManualCursorSaveFailure)
        {
            _cursorStore = cursorStore ?? new InMemoryReliablePushCursorStore();
            _onManualCursorSaveFailure = onManualCursorSaveFailure;
        }

        public string? CurrentSessionId { get; private set; }
        public long LastReceivedSequence { get { lock (_gate) return _tracker.LastReceivedSequence; } }

        public void StartSession(string sessionId, long lastReceivedSequence = 0)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session id is required.", nameof(sessionId));
            lock (_gate)
            {
                _generation++;
                _saveFailure = null;
                if (!StringComparer.Ordinal.Equals(CurrentSessionId, sessionId))
                {
                    _tracker.Reset();
                    _savedSequence = 0;
                }
                CurrentSessionId = sessionId;
                _tracker.MarkReceived(lastReceivedSequence);
                _ready.TrySetResult(true);
            }
        }

        public async ValueTask StartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var sequence = await LoadSessionSequenceAsync(sessionId, cancellationToken).ConfigureAwait(false);
            StartSession(sessionId, sequence);
        }

        internal async ValueTask<long> LoadSessionSequenceAsync(string sessionId, CancellationToken cancellationToken)
        {
            await _commitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var sequence = await _cursorStore.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return sequence;
            }
            finally { _commitGate.Release(); }
        }

        internal void BindConnection()
        {
            lock (_gate)
            {
                _generation++;
                _saveFailure = null;
            }
        }

        internal bool IsCurrentFailure(ReliablePushCursorSaveException failure)
        {
            lock (_gate) return ReferenceEquals(_saveFailure, failure);
        }

        internal async ValueTask<int> WaitForSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            Task ready;
            lock (_gate) ready = _ready.Task;
            var canceled = NewReady();
            using (cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken)))
                await await Task.WhenAny(ready, canceled.Task).ConfigureAwait(false);
            lock (_gate)
            {
                if (!StringComparer.Ordinal.Equals(CurrentSessionId, sessionId))
                    throw new InvalidOperationException("Reliable push metadata belongs to a different session.");
                return _generation;
            }
        }

        public ReliablePushApplyDecision Decide(ReliablePushSequence sequence)
        {
            lock (_gate) { EnsureStarted(); return _tracker.Decide(sequence.Value); }
        }

        // The enqueue delegate must synchronously transfer ownership to the inbound queue.
        // Only its success, never business completion, advances the receive cursor.
        public ValueTask<ReliablePushProcessResult> ReceiveAsync(
            ReliablePushMetadata metadata, Action enqueue,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken = default) =>
            ObserveManualReceiptAsync(ReceiveCoreAsync(metadata, enqueue, acknowledgeAsync, cancellationToken));

        internal ValueTask<ReliablePushProcessResult> ReceiveCoreAsync(
            ReliablePushMetadata metadata, Action enqueue,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken, int? expectedGeneration = null)
        {
            if (enqueue is null) throw new ArgumentNullException(nameof(enqueue));
            if (acknowledgeAsync is null) throw new ArgumentNullException(nameof(acknowledgeAsync));
            Task turn;
            int generation;
            ReliablePushApplyDecision decision;
            lock (_gate)
            {
                if (expectedGeneration.HasValue && expectedGeneration.Value != _generation)
                    return default;
                var session = EnsureStarted();
                if (!StringComparer.Ordinal.Equals(session, metadata.SessionId))
                    throw new InvalidOperationException("Reliable push metadata belongs to a different session.");
                cancellationToken.ThrowIfCancellationRequested();
                if (_saveFailure is not null)
                    return new ValueTask<ReliablePushProcessResult>(Task.FromException<ReliablePushProcessResult>(_saveFailure));
                decision = _tracker.Decide(metadata.Sequence.Value);
                if (decision.ShouldApply)
                {
                    enqueue();
                    _tracker.MarkReceived(metadata.Sequence.Value);
                }
                if (!decision.ShouldAck) return new ValueTask<ReliablePushProcessResult>(new ReliablePushProcessResult(decision, null));
                generation = _generation;
                turn = _commitGate.WaitAsync(cancellationToken);
            }
            return CommitAsync(turn, generation, metadata.SessionId, decision, acknowledgeAsync, cancellationToken);
        }

        private async ValueTask<ReliablePushProcessResult> CommitAsync(Task turn, int generation, string session,
            ReliablePushApplyDecision decision,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken)
        {
            await turn.ConfigureAwait(false);
            try
            {
                var sequence = await SaveReceivedSequenceAsync(generation, session, cancellationToken).ConfigureAwait(false);
                if (!sequence.HasValue) return new ReliablePushProcessResult(decision, null);
                cancellationToken.ThrowIfCancellationRequested();
                var ack = await acknowledgeAsync(new ReliablePushAckRequest(session, ReliablePushSequence.From(sequence.Value)),
                    cancellationToken).ConfigureAwait(false);
                return new ReliablePushProcessResult(decision, ack);
            }
            finally { _commitGate.Release(); }
        }

        // Only the holder of _commitGate writes. A retry can cover all contiguous admissions
        // made during backoff; a duplicate still resubmits its ACK if the wire reply was lost.
        private async ValueTask<long?> SaveReceivedSequenceAsync(int generation, string session, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long sequence;
                lock (_gate)
                {
                    if (generation != _generation) return null;
                    if (_saveFailure is not null) throw _saveFailure;
                    sequence = _tracker.LastReceivedSequence;
                    if (sequence <= _savedSequence) return sequence;
                }
                try
                {
                    await _cursorStore.SaveAsync(session, sequence, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (generation != _generation) return null;
                        if (attempt == MaxSaveAttempts)
                        {
                            _saveFailure = new ReliablePushCursorSaveException(error);
                            throw _saveFailure;
                        }
                    }
                    await Task.Delay(TimeSpan.FromTicks(SaveRetryDelay.Ticks * attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                lock (_gate)
                {
                    if (generation != _generation) return null;
                    _savedSequence = sequence;
                    return sequence;
                }
            }
        }

        public ValueTask<ReliablePushProcessResult> ProcessAsync<TPayload>(ReliablePushSequence sequence, TPayload payload,
            Func<TPayload, CancellationToken, ValueTask> applyAsync,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken = default)
        {
            if (applyAsync is null) throw new ArgumentNullException(nameof(applyAsync));
            return ProcessAsync(new ReliablePushMetadata(EnsureStarted(), sequence, "notification"),
                ct => applyAsync(payload, ct), acknowledgeAsync, cancellationToken);
        }

        public async ValueTask<ReliablePushProcessResult> ProcessAsync(ReliablePushMetadata metadata,
            Func<CancellationToken, ValueTask> applyAsync,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken = default)
        {
            if (applyAsync is null) throw new ArgumentNullException(nameof(applyAsync));
            var apply = false;
            var receipt = ReceiveAsync(metadata, () => apply = true, acknowledgeAsync, cancellationToken);
            ReliablePushProcessResult result = default;
            try { if (apply) await applyAsync(cancellationToken); }
            finally { result = await receipt.ConfigureAwait(false); }
            return result;
        }

        // A manual caller owns its business callback. Notify the session owner as soon as
        // storage fails, even while that callback is suspended, and preserve the public error.
        private async ValueTask<ReliablePushProcessResult> ObserveManualReceiptAsync(ValueTask<ReliablePushProcessResult> receipt)
        {
            try { return await receipt.ConfigureAwait(false); }
            catch (ReliablePushCursorSaveException error)
            {
                _onManualCursorSaveFailure?.Invoke(error);
                ExceptionDispatchInfo.Capture(error.InnerException!).Throw();
                throw;
            }
        }

        public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            string? session;
            Task turn;
            lock (_gate)
            {
                session = CurrentSessionId;
                Reset();
                turn = _commitGate.WaitAsync(cancellationToken);
            }
            await turn.ConfigureAwait(false);
            try
            {
                if (session is not null) await _cursorStore.ClearAsync(session, cancellationToken).ConfigureAwait(false);
            }
            finally { _commitGate.Release(); }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _generation++;
                _saveFailure = null;
                _savedSequence = 0;
                CurrentSessionId = null;
                _tracker.Reset();
                _ready.TrySetCanceled();
                _ready = NewReady();
            }
        }

        private string EnsureStarted() => CurrentSessionId ?? throw new InvalidOperationException("Reliable push session has not started.");
        private static TaskCompletionSource<bool> NewReady() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
