using System;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Client
{
    /// <summary>
    ///     Configuration for a generated <c>LakonaGameClient</c>, including RPC transport
    ///     settings and game-framework heartbeat behavior.
    /// </summary>
    public sealed class LakonaGameClientOptions : RpcClientOptions
    {
        /// <summary>
        ///     Creates game client options from a transport and serializer.
        /// </summary>
        public LakonaGameClientOptions(ITransport transport, IRpcSerializer serializer)
            : base(transport, serializer)
        {
        }

        /// <summary>
        ///     Enables or disables the game-layer heartbeat loop. This is separate from
        ///     <see cref="RpcClientOptions.KeepAlive"/>, which controls transport-level ping/pong.
        /// </summary>
        public bool HeartbeatEnabled { get; set; } = true;

        /// <summary>
        ///     Interval between game heartbeat RPC calls after a session is started.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        ///     Maximum time to wait for a game heartbeat RPC response before treating the heartbeat as failed.
        /// </summary>
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);

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
