using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Client
{
    internal sealed class LakonaGameHeartbeatLoop : IAsyncDisposable
    {
        private readonly RpcClientRuntime _rpcClient;
        private readonly LakonaGameClientCore _core;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _timeout;
        private int _started;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        public LakonaGameHeartbeatLoop(
            RpcClientRuntime rpcClient,
            LakonaGameClientCore core,
            TimeSpan interval,
            TimeSpan timeout)
        {
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _interval = interval;
            _timeout = timeout;
        }

        public void Start()
        {
            if (_interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_interval),
                    _interval,
                    "Lakona game heartbeat interval must be greater than zero.");
            }

            if (_timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_timeout),
                    _timeout,
                    "Lakona game heartbeat timeout must be greater than zero.");
            }

            if (_timeout < _interval)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_timeout),
                    _timeout,
                    "Lakona game heartbeat timeout must not be shorter than interval.");
            }

            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                throw new InvalidOperationException("Lakona game heartbeat loop already started.");
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunAsync(_cts.Token));
        }

        internal async ValueTask SendOnceAsync(CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                var snapshot = _core.Snapshot;
                var requestPayload = LakonaInternalCodec.EncodeGameHeartbeatRequest(new GameHeartbeatRequest
                {
                    SessionId = snapshot.SessionId
                });
                using var responsePayload = await _rpcClient
                    .CallRawAsync(
                        GameHeartbeatRpcIds.ServiceId,
                        GameHeartbeatRpcIds.HeartbeatMethodId,
                        requestPayload,
                        timeoutCts.Token)
                    .ConfigureAwait(false);
                var reply = LakonaInternalCodec.DecodeGameHeartbeatReply(responsePayload.Memory);

                switch (reply.Status)
                {
                    case GameHeartbeatStatus.Ok:
                        return;
                    case GameHeartbeatStatus.StateLost:
                        _core.MarkStateLost();
                        return;
                    case GameHeartbeatStatus.Terminated:
                        _core.ApplySessionTerminationNotice(
                            new SessionTerminationNotice(SessionTerminationReason.Policy, reply.Message));
                        return;
                    default:
                        _core.MarkReconnecting();
                        return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _core.MarkReconnecting();
            }
        }

        public async ValueTask DisposeAsync()
        {
            var cts = _cts;
            var loopTask = _loopTask;
            _cts = null;
            _loopTask = null;

            if (cts is null)
            {
                return;
            }

            cts.Cancel();
            if (loopTask is not null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            cts.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
                await SendOnceAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
