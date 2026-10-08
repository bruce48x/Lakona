# Lakona.Rpc.Transport.Loopback

In-memory paired RPC transport for local tests. Use it to exercise the client
and server without an operating-system socket.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Loopback
```

## Usage

```csharp
LoopbackTransport.CreatePair(out var client, out var server);
```

Import `Lakona.Rpc.Transport.Loopback`. The overload accepting `queueCapacity`
supports deterministic backpressure tests.

See the [Loopback contract](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/transport-contract.md#loopback) for
queue bounds, cancellation, pair disposal, and verification limits.
