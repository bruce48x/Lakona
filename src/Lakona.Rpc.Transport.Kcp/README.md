# Lakona.Rpc.Transport.Kcp

KCP client and server transport for Lakona.Rpc. Use `KcpTransport` on the client
and `KcpConnectionAcceptor` with the server host.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Kcp
```

## Client Construction

```csharp
var transport = new KcpTransport("127.0.0.1", 20000);
var assigned = new KcpTransport("127.0.0.1", 20000, conversationId: 1234);
```

Import `Lakona.Rpc.Transport.Kcp`; pass the selected transport to client options.

## Guides

- [KCP transport contract](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/transport-contract.md#kcp): establishment, identity, buffering, cancellation, and rejection.
- [RPC hosting](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md): server composition and admission.
- [Game client integration](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-integrate-game-client/SKILL.md): recovery-capable transport factories.
