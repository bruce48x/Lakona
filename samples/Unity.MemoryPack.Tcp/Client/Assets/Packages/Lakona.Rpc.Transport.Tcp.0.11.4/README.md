# Lakona.Rpc.Transport.Tcp

TCP transport implementations for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Tcp
```

## Documentation

API reference: https://bruce48x.github.io/Lakona.Rpc/reference/api/

Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/

## Includes

- `TcpTransport` (client)
- `TcpServerTransport` (server)
- `TcpConnectionAcceptor` (server)

## Server Usage

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseAcceptor(new TcpConnectionAcceptor(20000));

await builder.RunAsync();
```
