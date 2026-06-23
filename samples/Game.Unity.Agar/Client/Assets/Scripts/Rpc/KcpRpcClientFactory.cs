#nullable enable

using System;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;

namespace Rpc
{
    public static class KcpRpcClientFactory
    {
        public static RpcClientOptions CreateOptions(string host, int port)
        {
            return new RpcClientOptions(
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
