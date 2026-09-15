# Lakona.Rpc.Serializer.MemoryPack

MemoryPack based payload serializer for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Serializer.MemoryPack
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Usage

```csharp
using Lakona.Rpc.Serializer.MemoryPack;

var serializer = new MemoryPackRpcSerializer();
```

Pass `MemoryPackSerializerOptions` when the host needs non-default MemoryPack
options:

```csharp
var serializer = new MemoryPackRpcSerializer(options);
```

Use it with `Lakona.Rpc.Server` by passing the serializer instance explicitly:

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseAcceptor(new TcpConnectionAcceptor(20000));

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
