using System;
using Lakona.Rpc.Client;

namespace Lakona.Game.Client
{
    public sealed class LakonaGameClientOptions
    {
        public LakonaGameClientOptions(RpcClientOptions rpcOptions)
        {
            RpcOptions = rpcOptions ?? throw new ArgumentNullException(nameof(rpcOptions));
        }

        public RpcClientOptions RpcOptions { get; }

        public string? ClientRuntime { get; set; }

        public string? Platform { get; set; }

        public string? GameVersion { get; set; }

        public bool HeartbeatEnabled { get; set; } = true;

        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);
    }
}
