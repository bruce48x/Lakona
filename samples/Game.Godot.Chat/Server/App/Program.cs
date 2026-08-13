using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;
using Microsoft.Extensions.Logging;

return await LakonaGameServer.RunAsync(args, static server => server
    .ConfigureLogging(static logging => logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }))
    .RegisterEndpointTransport("websocket", static async (endpoint, cancellationToken) =>
        await WsConnectionAcceptor.CreateAsync(
            endpoint.Port,
            string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
            endpoint.Host,
            cancellationToken).ConfigureAwait(false))
    .RegisterEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer()));
