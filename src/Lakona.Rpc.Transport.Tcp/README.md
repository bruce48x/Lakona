# Lakona.Rpc.Transport.Tcp

TCP client and server transport for Lakona.Rpc. Use `TcpTransport` on the
client and `TcpConnectionAcceptor` with the server host.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Tcp
```

## Guides

- [Transport contract](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/transport-contract.md): initialization, framing ownership, concurrency, cancellation, and shutdown.
- [RPC hosting](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md): server composition and lifecycle.
- [Game client integration](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-integrate-game-client/SKILL.md): recovery-capable transport factories.
