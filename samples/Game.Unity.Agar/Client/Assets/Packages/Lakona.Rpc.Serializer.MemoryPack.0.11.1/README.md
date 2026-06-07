# Lakona.Rpc.Serializer.MemoryPack

MemoryPack based payload serializer for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Serializer.MemoryPack
```

## Usage

```csharp
using Lakona.Rpc.Serializer.MemoryPack;

var serializer = new MemoryPackRpcSerializer();
```

Use it with `Lakona.Rpc.Server` by passing the serializer instance explicitly:

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseAcceptor(new TcpConnectionAcceptor(20000));

await builder.RunAsync();
```
