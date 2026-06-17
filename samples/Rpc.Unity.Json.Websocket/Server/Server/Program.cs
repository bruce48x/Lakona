using Lakona.Rpc.Core;
using System;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.WebSocket;

var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new JsonRpcSerializer())
    .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));

builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(
    20000,
    "/ws",
    "127.0.0.1",
    builder.Limits.MaxPendingAcceptedConnections,
    ct));

await builder.RunAsync();
