#nullable enable

using System;
using Lakona.Game.Client;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

namespace Game.Unity.MMO.Client.Rpc
{
    public static class WebSocketClientFactory
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

        public static string BuildUrl(string host, int port, string path)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/ws" : path.StartsWith("/") ? path : "/" + path;
            return $"ws://{host}:{port}{normalizedPath}";
        }
    }
}
