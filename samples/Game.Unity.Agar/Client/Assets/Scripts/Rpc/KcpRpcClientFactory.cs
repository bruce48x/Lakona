#nullable enable

using System;
using Lakona.Game.Client;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
#if UNITY_INCLUDE_TESTS
using Rpc.Testing;
#endif

namespace Rpc
{
    public static class KcpRpcClientFactory
    {
        public static LakonaGameClientOptions CreateOptions(string host, int port)
        {
            return new LakonaGameClientOptions(
                () => new KcpTransport(host, port),
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
#if UNITY_INCLUDE_TESTS
        public static LakonaGameClientOptions CreateOptions(string host, int port, TestTransportGate gate)
        {
            return new LakonaGameClientOptions(
                () => gate.Wrap(new KcpTransport(host, port)),
                new MemoryPackRpcSerializer());
        }
#endif
    }
}
