# Lakona.Rpc.Serializer.Json

`System.Text.Json` based payload serializer for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Serializer.Json
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Usage

```csharp
using Lakona.Rpc.Serializer.Json;

var serializer = new JsonRpcSerializer();
```

Use it with `Lakona.Rpc.Server` by passing the serializer instance explicitly:

```csharp
var builder = RpcServerHostBuilder.Create()
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
