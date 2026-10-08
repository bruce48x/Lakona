# Lakona.Rpc.Transport.Kcp

KCP transport primitives for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Kcp
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Includes

- `KcpTransport`
- `KcpListener`
- `KcpAcceptResult`
- `KcpServerTransport`
- `KcpConnectionAcceptor`

## Server Usage

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer());

builder.UseAcceptor(new KcpConnectionAcceptor(
    20000,
    builder.Limits.MaxPendingAcceptedConnections));

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

The server listener keeps slow-consumer buffering inside each connection's KCP
receive window. It does not pre-decode an unbounded application frame queue or
block the shared UDP listener while waiting for application admission or one
RPC Session to read. Configure application and framework admission through
`RpcServerHostBuilder.UseSessionAdmissionGate` rather than the transport
handshake.

## Client Usage

`KcpTransport` can now either generate its own conversation id or reuse a server-assigned `conv`:

```csharp
var generatedConv = new KcpTransport("127.0.0.1", 20001);
var assignedConv = new KcpTransport("127.0.0.1", 20001, conversationId: 1234);
```

`ConnectAsync` owns a finite 10-second establishment deadline and retransmits
the bootstrap request every 250 milliseconds until it receives a matching
response. A caller cancellation token may end the attempt earlier. A silent or
unreachable listener produces `TimeoutException`; a listener whose pending
connection capacity is full responds immediately with
`KcpConnectionRejectedException`.

The transport rejection describes only KCP connection establishment. RPC
Session admission and Game Session recovery remain owned by their respective
higher-level frameworks.

The server identifies a KCP connection by remote UDP endpoint plus conversation
id. Repeated handshakes for the same identity are idempotent; a new conversation
from the same endpoint establishes a separate transport and RPC Session within
the existing pending and active connection limits. KCP data is routed only to
the exact matching identity, and disposing one conversation does not replace or
terminate another.
