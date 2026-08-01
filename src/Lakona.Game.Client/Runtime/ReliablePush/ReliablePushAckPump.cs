using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Rpc.Client;

namespace Lakona.Game.Client.ReliablePush
{
    internal sealed class ReliablePushAckPump : IAsyncDisposable
    {
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Action<ReliablePushAckOutcome> _applyOutcome;
        private readonly Action _markFailed;
        private RpcClientRuntime? _runtime;
        private string? _sessionId;
        private long _activeSequence;
        private long _pendingSequence;
        private TimeSpan _timeout;
        private Task? _loop;
        private CancellationTokenSource? _activeSend;
        private int _generation;
        private bool _disposed;

        public ReliablePushAckPump(
            Action<ReliablePushAckOutcome> applyOutcome,
            Action markFailed)
        {
            _applyOutcome = applyOutcome ?? throw new ArgumentNullException(nameof(applyOutcome));
            _markFailed = markFailed ?? throw new ArgumentNullException(nameof(markFailed));
        }

        public ReliablePushAckOutcome Queue(
            RpcClientRuntime runtime,
            ReliablePushAckRequest acknowledgement,
            TimeSpan timeout)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

            lock (_gate)
            {
                if (_disposed)
                {
                    return ReliablePushAckOutcome.Accepted();
                }

                if (!ReferenceEquals(_runtime, runtime) ||
                    !StringComparer.Ordinal.Equals(_sessionId, acknowledgement.SessionId))
                {
                    _generation++;
                    _activeSend?.Cancel();
                    _runtime = runtime;
                    _sessionId = acknowledgement.SessionId;
                    _activeSequence = 0;
                    _pendingSequence = 0;
                }

                if (acknowledgement.Sequence.Value > _activeSequence &&
                    acknowledgement.Sequence.Value > _pendingSequence)
                {
                    _pendingSequence = acknowledgement.Sequence.Value;
                }

                _timeout = timeout;
                if (_loop is null)
                {
                    _loop = Task.Run(RunAsync);
                }

                Signal();
            }

            return ReliablePushAckOutcome.Accepted();
        }

        public async ValueTask DisposeAsync()
        {
            Task? loop;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pendingSequence = 0;
                _activeSend?.Cancel();
                _lifetime.Cancel();
                loop = _loop;
                Signal();
            }

            if (loop is not null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            lock (_gate)
            {
                _activeSend?.Dispose();
                _activeSend = null;
            }
            _signal.Dispose();
            _lifetime.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await _signal.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                while (TryTake(out var send))
                {
                    try
                    {
                        var outcome = await SendAsync(send).ConfigureAwait(false);
                        if (IsCurrent(send))
                        {
                            _applyOutcome(outcome);
                            if (outcome.Status != ReliablePushAckStatus.Accepted &&
                                outcome.Status != ReliablePushAckStatus.Duplicate)
                            {
                                ClearPending(send.Generation);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (
                        _lifetime.IsCancellationRequested || !IsCurrent(send))
                    {
                    }
                    catch
                    {
                        if (IsCurrent(send))
                        {
                            ClearPending(send.Generation);
                            _markFailed();
                        }
                    }
                    finally
                    {
                        ClearActiveSend(send.Cancellation);
                    }
                }
            }
        }

        private bool TryTake(out PendingAck send)
        {
            lock (_gate)
            {
                if (_disposed || _runtime is null || _sessionId is null || _pendingSequence <= 0)
                {
                    send = default;
                    return false;
                }

                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                cancellation.CancelAfter(_timeout);
                _activeSend = cancellation;
                send = new PendingAck(
                    _runtime,
                    new ReliablePushAckRequest(
                        _sessionId,
                        ReliablePushSequence.From(_pendingSequence)),
                    _generation,
                    cancellation);
                _activeSequence = _pendingSequence;
                _pendingSequence = 0;
                return true;
            }
        }

        private static async ValueTask<ReliablePushAckOutcome> SendAsync(PendingAck send)
        {
            var payload = LakonaInternalCodec.EncodeReliablePushAckRequest(send.Acknowledgement);
            using var response = await send.Runtime.CallRawAsync(
                GameReliablePushRpcIds.ServiceId,
                GameReliablePushRpcIds.AckMethodId,
                payload,
                send.Cancellation.Token).ConfigureAwait(false);
            return LakonaInternalCodec.DecodeReliablePushAckOutcome(response.Memory);
        }

        private bool IsCurrent(PendingAck send)
        {
            lock (_gate)
            {
                return !_disposed &&
                       send.Generation == _generation &&
                       ReferenceEquals(send.Runtime, _runtime) &&
                       StringComparer.Ordinal.Equals(send.Acknowledgement.SessionId, _sessionId);
            }
        }

        private void ClearPending(int generation)
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _pendingSequence = 0;
                }
            }
        }

        private void ClearActiveSend(CancellationTokenSource cancellation)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeSend, cancellation))
                {
                    _activeSend = null;
                    _activeSequence = 0;
                }
            }
            cancellation.Dispose();
        }

        private void Signal()
        {
            if (_signal.CurrentCount == 0)
            {
                try
                {
                    _signal.Release();
                }
                catch (SemaphoreFullException)
                {
                }
            }
        }

        private readonly struct PendingAck
        {
            public PendingAck(
                RpcClientRuntime runtime,
                ReliablePushAckRequest acknowledgement,
                int generation,
                CancellationTokenSource cancellation)
            {
                Runtime = runtime;
                Acknowledgement = acknowledgement;
                Generation = generation;
                Cancellation = cancellation;
            }

            public RpcClientRuntime Runtime { get; }

            public ReliablePushAckRequest Acknowledgement { get; }

            public int Generation { get; }

            public CancellationTokenSource Cancellation { get; }
        }
    }
}
