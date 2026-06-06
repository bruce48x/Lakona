#nullable enable

using System;
using Rpc.Generated;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;

namespace Rpc
{
    public static class KcpRpcClientFactory
    {
        public static RpcClient Create(string host, int port, RpcClient.RpcNotificationBindings callbacks)
        {
            return new RpcClient(
                new RpcClientOptions(
                    new KcpTransport(host, port),
                    new MemoryPackRpcSerializer())
                {
                    KeepAlive = new RpcKeepAliveOptions
                    {
                        Enabled = true,
                        Interval = TimeSpan.FromSeconds(2),
                        Timeout = TimeSpan.FromSeconds(6)
                    }
                },
                callbacks);
        }
    }
}
