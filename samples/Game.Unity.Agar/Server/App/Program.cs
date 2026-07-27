using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.WebSocket;

return await LakonaGameServer.RunAsync(args, static server => server
    .RegisterEndpointTransport("websocket", static async (endpoint, cancellationToken) =>
        await WsConnectionAcceptor.CreateAsync(
            endpoint.Port,
            string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
            endpoint.Host,
            cancellationToken).ConfigureAwait(false))
    .RegisterEndpointTransport("kcp", static endpoint =>
        new KcpConnectionAcceptor(endpoint.Port, endpoint.Host))
    .RegisterEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer()));
