# Lakona.Rpc.Transport.Loopback

In-memory loopback transport for local tests in Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Loopback
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Includes

- `LoopbackTransport.CreatePair(out client, out server)`
- `LoopbackTransport.CreatePair(out client, out server, queueCapacity)` for
  deterministic backpressure tests

Each direction has a bounded queue with a default capacity of 256 frames.
Sending waits for capacity and observes cancellation. Disposing either endpoint
closes the whole pair: both endpoints become disconnected, pending receives
observe EOF, and pending or later sends are rejected.
