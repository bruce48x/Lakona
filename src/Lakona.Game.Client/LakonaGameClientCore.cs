using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Client
{
    public sealed class LakonaGameClientCore : IAsyncDisposable
    {
        private readonly ClientSessionController _sessions;
        private readonly ReliablePushInbox _reliablePush;
        private readonly object _heartbeatLock = new object();
        private LakonaGameHeartbeatLoop? _heartbeat;

        public LakonaGameClientCore(IReliablePushCursorStore? cursorStore = null)
        {
            _reliablePush = new ReliablePushInbox(cursorStore);
            _sessions = new ClientSessionController(_reliablePush);
        }

        public ClientSessionSnapshot Snapshot
        {
            get { return _sessions.Snapshot; }
        }

        public bool ReliablePushEnabled { get; private set; } = true;

        public bool ReliablePushAckRequired { get; private set; } = true;

        public void ApplyServerHello(GameServerHello hello)
        {
            if (hello == null) throw new ArgumentNullException(nameof(hello));
            ReliablePushEnabled = hello.ReliablePush.Enabled;
            ReliablePushAckRequired = hello.ReliablePush.Enabled && hello.ReliablePush.AckRequired;
        }

        public void StartHeartbeat(RpcClientRuntime rpcClient, LakonaGameClientOptions options)
        {
            if (rpcClient == null) throw new ArgumentNullException(nameof(rpcClient));
            if (options == null) throw new ArgumentNullException(nameof(options));

            lock (_heartbeatLock)
            {
                if (_heartbeat is not null)
                {
                    throw new InvalidOperationException("Lakona game heartbeat loop already started.");
                }

                var heartbeat = new LakonaGameHeartbeatLoop(rpcClient, this, options);
                heartbeat.Start();
                _heartbeat = heartbeat;
            }
        }

        public async ValueTask<GameServerHello> HandshakeAsync(
            RpcClientRuntime rpcClient,
            GameClientHello hello,
            CancellationToken cancellationToken = default)
        {
            if (rpcClient == null) throw new ArgumentNullException(nameof(rpcClient));
            if (hello == null) throw new ArgumentNullException(nameof(hello));

            var requestPayload = LakonaInternalCodec.EncodeGameClientHello(hello);
            using var responsePayload = await rpcClient.CallRawAsync(
                GameHandshakeRpcIds.ServiceId,
                GameHandshakeRpcIds.HandshakeMethodId,
                requestPayload,
                cancellationToken).ConfigureAwait(false);
            var serverHello = LakonaInternalCodec.DecodeGameServerHello(responsePayload.Memory);
            ApplyServerHello(serverHello);
            return serverHello;
        }

        public void MarkConnecting()
        {
            _sessions.MarkConnecting();
        }

        public void MarkReady()
        {
            _sessions.MarkReady();
        }

        public void MarkConnectionFailed(ClientConnectionFailure failure)
        {
            _sessions.MarkConnectionFailed(failure);
        }

        public void StartSession(string sessionId, long lastReliableSequence = 0)
        {
            _sessions.StartSession(sessionId, lastReliableSequence);
        }

        public async ValueTask StartSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            if (Snapshot.Phase == ClientSessionPhase.ConnectionFailed)
            {
                return;
            }

            await _reliablePush.StartSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (Snapshot.Phase == ClientSessionPhase.ConnectionFailed)
            {
                _reliablePush.Reset();
                return;
            }

            _sessions.StartSession(sessionId, _reliablePush.LastAppliedSequence);
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
            Func<ReliablePushAckRequest, CancellationToken, ValueTask<ReliablePushAckOutcome>> acknowledgeAsync,
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

        public async ValueTask DisposeAsync()
        {
            LakonaGameHeartbeatLoop? heartbeat;
            lock (_heartbeatLock)
            {
                heartbeat = _heartbeat;
                _heartbeat = null;
            }

            if (heartbeat is not null)
            {
                await heartbeat.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
