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
        private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(15);
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(45);

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

        public TimeSpan HeartbeatInterval
        {
            get { return _heartbeatInterval; }
        }

        public TimeSpan HeartbeatTimeout
        {
            get { return _heartbeatTimeout; }
        }

        public void ApplyServerHello(GameServerHello hello)
        {
            if (hello == null) throw new ArgumentNullException(nameof(hello));
            if (hello.SelectedProtocolVersion != 1)
            {
                throw new InvalidOperationException(
                    $"Unsupported Lakona game handshake protocol version '{hello.SelectedProtocolVersion}'.");
            }

            ReliablePushEnabled = hello.ReliablePush.Enabled;
            ReliablePushAckRequired = hello.ReliablePush.Enabled && hello.ReliablePush.AckRequired;
            ApplyHeartbeatPolicy(hello.Heartbeat ?? new GameHeartbeatHandshakeSettings());
        }

        public void StartHeartbeat(RpcClientRuntime rpcClient)
        {
            if (rpcClient == null) throw new ArgumentNullException(nameof(rpcClient));

            lock (_heartbeatLock)
            {
                if (_heartbeat is not null)
                {
                    throw new InvalidOperationException("Lakona game heartbeat loop already started.");
                }

                var heartbeat = new LakonaGameHeartbeatLoop(
                    rpcClient,
                    this,
                    _heartbeatInterval,
                    _heartbeatTimeout);
                heartbeat.Start();
                _heartbeat = heartbeat;
            }
        }

        private void ApplyHeartbeatPolicy(GameHeartbeatHandshakeSettings heartbeat)
        {
            if (heartbeat.Interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    "Heartbeat.Interval",
                    heartbeat.Interval,
                    "Lakona game heartbeat interval must be greater than zero.");
            }

            if (heartbeat.Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    "Heartbeat.Timeout",
                    heartbeat.Timeout,
                    "Lakona game heartbeat timeout must be greater than zero.");
            }

            if (heartbeat.Timeout < heartbeat.Interval)
            {
                throw new ArgumentOutOfRangeException(
                    "Heartbeat.Timeout",
                    heartbeat.Timeout,
                    "Lakona game heartbeat timeout must not be shorter than interval.");
            }

            _heartbeatInterval = heartbeat.Interval;
            _heartbeatTimeout = heartbeat.Timeout;
        }

        public void BindReliablePush(RpcClientRuntime rpcClient)
        {
            if (rpcClient == null) throw new ArgumentNullException(nameof(rpcClient));

            rpcClient.SetNotificationDispatchMiddleware(async (metadata, next) =>
            {
                if (metadata is null ||
                    !StringComparer.Ordinal.Equals(metadata.Type, LakonaInternalCodec.ReliablePushMetadataType))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                var reliableMetadata = LakonaInternalCodec.DecodeReliablePushMetadata(metadata.Payload);
                var result = await _reliablePush.ProcessAsync(
                    reliableMetadata,
                    _ => next(),
                    (ack, cancellationToken) => SendReliablePushAckAsync(rpcClient, ack, cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);

                if (result.Acknowledgement.HasValue)
                {
                    _sessions.ApplyAckOutcome(result.Acknowledgement.Value);
                }
            });
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

        public void StartSession(string sessionId, long sessionGeneration, long lastReliableSequence)
        {
            _sessions.StartSessionWithGeneration(sessionId, sessionGeneration, lastReliableSequence);
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
            if (Snapshot.Phase == ClientSessionPhase.ConnectionFailed)
            {
                return;
            }

            await _reliablePush
                .StartSessionAsync(sessionId, sessionGeneration, cancellationToken)
                .ConfigureAwait(false);
            if (Snapshot.Phase == ClientSessionPhase.ConnectionFailed)
            {
                _reliablePush.Reset();
                return;
            }

            _sessions.StartSessionWithGeneration(
                sessionId,
                sessionGeneration,
                _reliablePush.LastAppliedSequence);
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

        private static async ValueTask<ReliablePushAckOutcome> SendReliablePushAckAsync(
            RpcClientRuntime rpcClient,
            ReliablePushAckRequest ack,
            CancellationToken cancellationToken)
        {
            var payload = LakonaInternalCodec.EncodeReliablePushAckRequest(ack);
            using var response = await rpcClient.CallRawAsync(
                GameReliablePushRpcIds.ServiceId,
                GameReliablePushRpcIds.AckMethodId,
                payload,
                cancellationToken).ConfigureAwait(false);
            return LakonaInternalCodec.DecodeReliablePushAckOutcome(response.Memory);
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
