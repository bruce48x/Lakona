#nullable enable

using System;
using Lakona.Game.Client;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;
#if UNITY_INCLUDE_TESTS
using Rpc.Testing;
#endif

namespace Rpc
{
    public static class WebSocketRpcClientFactory
    {
        public static LakonaGameClientOptions CreateOptions(string host, int port, string path)
        {
            return new LakonaGameClientOptions(
                () => new WsTransport(BuildUrl(host, port, path)),
                new MemoryPackRpcSerializer())
            {
                KeepAlive = new RpcKeepAliveOptions
                {
                    Enabled = true,
                    Interval = TimeSpan.FromSeconds(5),
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };
        }
#if UNITY_INCLUDE_TESTS
        public static LakonaGameClientOptions CreateOptions(string host, int port, string path, TestTransportGate gate)
        {
            return new LakonaGameClientOptions(
                () => gate.Wrap(new WsTransport(BuildUrl(host, port, path))),
                new MemoryPackRpcSerializer());
        }
#endif

        public static string BuildUrl(string host, int port, string path)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(path)
                ? "/ws"
                : path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;

            if (host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                return $"{host.TrimEnd('/')}{normalizedPath}";
            }

            return $"ws://{host}:{port}{normalizedPath}";
        }
    }
}
