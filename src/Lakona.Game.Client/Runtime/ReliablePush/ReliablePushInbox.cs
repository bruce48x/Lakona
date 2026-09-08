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
        private readonly object _gate = new object();
        private TaskCompletionSource<bool> _ready = NewReady();
        private Task _commitTail = Task.CompletedTask;
        private int _generation;

        public ReliablePushInbox(IReliablePushCursorStore? cursorStore = null)
        { _cursorStore = cursorStore ?? new InMemoryReliablePushCursorStore(); }

        public string? CurrentSessionId { get; private set; }
        public long LastReceivedSequence { get { lock (_gate) return _tracker.LastReceivedSequence; } }

        public void StartSession(string sessionId, long lastReceivedSequence = 0)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session id is required.", nameof(sessionId));
            lock (_gate)
            {
                _generation++;
                _commitTail = IgnoreFailureAsync(_commitTail);
                CurrentSessionId = sessionId;
                _tracker.Reset();
                _tracker.MarkReceived(lastReceivedSequence);
                _ready.TrySetResult(true);
            }
        }

        public async ValueTask StartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Task commits;
            lock (_gate) commits = _commitTail;
            try { await commits.ConfigureAwait(false); } catch { }
            var sequence = await _cursorStore.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            StartSession(sessionId, sequence);
        }

        internal async ValueTask WaitForSessionAsync(string sessionId, CancellationToken cancellationToken)
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
            CancellationToken cancellationToken = default)
        {
            if (enqueue is null) throw new ArgumentNullException(nameof(enqueue));
            if (acknowledgeAsync is null) throw new ArgumentNullException(nameof(acknowledgeAsync));
            lock (_gate)
            {
                var session = EnsureStarted();
                if (!StringComparer.Ordinal.Equals(session, metadata.SessionId))
                    throw new InvalidOperationException("Reliable push metadata belongs to a different session.");
                cancellationToken.ThrowIfCancellationRequested();
                var decision = _tracker.Decide(metadata.Sequence.Value);
                if (decision.ShouldApply)
                {
                    enqueue();
                    _tracker.MarkReceived(metadata.Sequence.Value);
                }
                if (!decision.ShouldAck) return new ValueTask<ReliablePushProcessResult>(new ReliablePushProcessResult(decision, null));
                var commit = CommitAsync(_commitTail, _generation, session, _tracker.LastReceivedSequence,
                    decision, acknowledgeAsync, cancellationToken);
                _commitTail = commit;
                return new ValueTask<ReliablePushProcessResult>(commit);
            }
        }

        private async Task<ReliablePushProcessResult> CommitAsync(Task previous, int generation, string session,
            long sequence, ReliablePushApplyDecision decision,
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
            CancellationToken cancellationToken)
        {
            await previous.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) if (generation != _generation) return new ReliablePushProcessResult(decision, null);
            await _cursorStore.SaveAsync(session, sequence, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) if (generation != _generation) return new ReliablePushProcessResult(decision, null);
            var ack = await acknowledgeAsync(new ReliablePushAckRequest(session, ReliablePushSequence.From(sequence)), cancellationToken).ConfigureAwait(false);
            return new ReliablePushProcessResult(decision, ack);
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

        public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            string? session;
            Task commits;
            lock (_gate) { session = CurrentSessionId; commits = _commitTail; Reset(); }
            try { await commits.ConfigureAwait(false); } catch { }
            if (session is not null) await _cursorStore.ClearAsync(session, cancellationToken).ConfigureAwait(false);
        }

        public void Reset()
        {
            lock (_gate)
            {
                _generation++;
                CurrentSessionId = null;
                _tracker.Reset();
                _ready.TrySetCanceled();
                _ready = NewReady();
            }
        }

        private static async Task IgnoreFailureAsync(Task task)
        {
            try { await task.ConfigureAwait(false); } catch { }
        }

        private string EnsureStarted() => CurrentSessionId ?? throw new InvalidOperationException("Reliable push session has not started.");
        private static TaskCompletionSource<bool> NewReady() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
