# Lakona.Rpc.Serializer.Json

`System.Text.Json` based payload serializer for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Serializer.Json
```

## Documentation

API reference: https://bruce48x.github.io/Lakona.Rpc/reference/api/

Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/

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
    builder.Limits.MaxPendingAcceptedConnections,
    ct));

await builder.RunAsync();
```
