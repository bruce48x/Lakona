# Lakona.Rpc.Transport.WebSocket

WebSocket client/server transport implementations for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.WebSocket
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Includes

- `WsTransport`
- `WsServerTransport`
- `WsConnectionAcceptor`

## Server Usage

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new JsonRpcSerializer());

builder.UseAcceptor(ct => WsConnectionAcceptor.CreateAsync(
    20000,
    "/ws",
    "127.0.0.1",
    builder.Limits.MaxPendingAcceptedConnections,
    ct));

using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    await builder.RunAsync(shutdown.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
```
