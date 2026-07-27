using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

return await LakonaGameServer.RunAsync(args, static server => server
    .RegisterEndpointTransport("websocket", static async (endpoint, cancellationToken) =>
        await WsConnectionAcceptor.CreateAsync(
            endpoint.Port,
            string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
            endpoint.Host,
            cancellationToken).ConfigureAwait(false))
    .RegisterEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer()));
