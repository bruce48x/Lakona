using System;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Client
{
    /// <summary>
    ///     Configuration for a generated <c>LakonaGameClient</c>, including RPC transport
    ///     settings. Game-framework heartbeat policy is supplied by the server handshake.
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
        ///     Configures compression or encryption for the client transport.
        /// </summary>
        public new LakonaGameClientOptions UseSecurity(Action<TransportSecurityConfig> configure)
        {
            base.UseSecurity(configure);
            return this;
        }
    }
}
