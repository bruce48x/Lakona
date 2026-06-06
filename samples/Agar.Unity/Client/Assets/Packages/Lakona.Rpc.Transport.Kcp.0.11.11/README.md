# Lakona.Rpc.Transport.Kcp

KCP transport primitives for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Kcp
```

## Documentation

API reference: https://bruce48x.github.io/Lakona.Rpc/reference/api/

Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/

## Includes

- `KcpTransport`
- `KcpListener`
- `KcpAcceptResult`
- `KcpServerTransport`
- `KcpConnectionAcceptor`
- `KcpHandshakeAdmission`

## Server Usage

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer());

builder.UseAcceptor(new KcpConnectionAcceptor(
    20000,
    builder.Limits.MaxPendingAcceptedConnections));

await builder.RunAsync();
```

You can optionally gate new KCP sessions by validating the handshake `conv` before accepting:

```csharp
builder.UseAcceptor(new KcpConnectionAcceptor(
    20001,
    builder.Limits.MaxPendingAcceptedConnections,
    (conv, remoteEndPoint, ct) => new ValueTask<bool>(conv != 0)));
```

## Client Usage

`KcpTransport` can now either generate its own conversation id or reuse a server-assigned `conv`:

```csharp
var generatedConv = new KcpTransport("127.0.0.1", 20001);
var assignedConv = new KcpTransport("127.0.0.1", 20001, conversationId: 1234);
```
