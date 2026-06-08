# Lakona.Rpc.Transport.WebSocket

WebSocket client/server transport implementations for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.WebSocket
```

## Documentation

API reference: https://bruce48x.github.io/Lakona.Rpc/reference/api/

Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/

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
    builder.Limits.MaxPendingAcceptedConnections,
    ct));

await builder.RunAsync();
```
