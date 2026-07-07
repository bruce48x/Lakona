#nullable enable

using System;
using Lakona.Game.Client;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;

namespace Rpc
{
    public static class KcpRpcClientFactory
    {
        public static LakonaGameClientOptions CreateOptions(string host, int port)
        {
            return new LakonaGameClientOptions(
                new KcpTransport(host, port),
                new MemoryPackRpcSerializer())
            {
                KeepAlive = new RpcKeepAliveOptions
                {
                    Enabled = true,
                    Interval = TimeSpan.FromSeconds(2),
                    Timeout = TimeSpan.FromSeconds(6)
                }
            };
        }
    }
}
