using System;
using System.Threading;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Game.Client.ReliablePush;

namespace Lakona.Game.Client
{
    /// <summary>
    ///     Configuration for a generated <c>LakonaGameClient</c>, including RPC transport
    ///     settings. Game-framework heartbeat policy is supplied by the server handshake.
    /// </summary>
    public sealed class LakonaGameClientOptions : RpcClientOptions
    {
        private readonly Func<ITransport>? _transportFactory;
        private int _transportCreated;

        public IGameSessionRecoveryScheduler RecoveryScheduler { get; set; } = new GameSessionRecoveryScheduler();
        /// <summary>
        ///     Creates game client options from a transport and serializer.
        /// </summary>
        public LakonaGameClientOptions(ITransport transport, IRpcSerializer serializer)
            : base(transport, serializer)
        {
        }

        public LakonaGameClientOptions(Func<ITransport> transportFactory, IRpcSerializer serializer)
            : base((transportFactory ?? throw new ArgumentNullException(nameof(transportFactory)))(), serializer)
        {
            _transportFactory = transportFactory;
            _transportCreated = 0;
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public LakonaGameClientOptions CreateConnectionGeneration()
        {
            var transport = Interlocked.Exchange(ref _transportCreated, 1) == 0
                ? Transport
                : _transportFactory?.Invoke()
                    ?? throw new InvalidOperationException(
                        "Automatic recovery requires LakonaGameClientOptions to be constructed with a transport factory.");
            var generation = new LakonaGameClientOptions(transport, Serializer)
            {
                KeepAlive = KeepAlive,
                LoggerFactory = LoggerFactory,
                ReliablePushCursorStore = ReliablePushCursorStore,
                RecoveryScheduler = RecoveryScheduler,
            };
            generation.Security.EnableCompression = Security.EnableCompression;
            generation.Security.CompressionThresholdBytes = Security.CompressionThresholdBytes;
            generation.Security.MaxDecodedFrameBytes = Security.MaxDecodedFrameBytes;
            generation.Security.EnableEncryption = Security.EnableEncryption;
            generation.Security.EncryptionKey = Security.EncryptionKey == null
                ? null
                : (byte[])Security.EncryptionKey.Clone();
            generation.Security.EncryptionKeyBase64 = Security.EncryptionKeyBase64;
            return generation;
        }

        /// <summary>
        /// Gets or sets the cursor store shared by replacement RPC clients that
        /// resume the same game session.
        /// </summary>
        public IReliablePushCursorStore? ReliablePushCursorStore { get; set; }

        /// <summary>
        ///     Configures compression or encryption for the client transport.
        /// </summary>
        public new LakonaGameClientOptions UseSecurity(Action<TransportSecurityConfig> configure)
        {
            base.UseSecurity(configure);
            return this;
        }
    }
}
