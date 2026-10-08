# Lakona.Rpc.Client

Transport-independent RPC client runtime. The project's generated `RpcClient`
provides typed APIs for its contract set. Choose transport and serializer
packages separately; use `Lakona.Game.Client` for Game session recovery.

## Install

```bash
dotnet add package Lakona.Rpc.Client
```

## Minimal Connection

```csharp
using Client.Generated;
using Lakona.Rpc.Client;

await using var client = new RpcClient(new RpcClientOptions(transport, serializer));
await client.ConnectAsync(cancellationToken);
```

Use the project's actual generated namespace and configured transport/serializer.

## Guides

- [RPC architecture](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md): connection lifecycle, ordered delivery, notification failures, and KeepAlive.
- [Source generation](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/source-generation.md): typed client APIs and callback bindings.
- [Transport contract](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/transport-contract.md): cancellation, shutdown, and reconnect ownership.
- [Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md): provider setup and lifetime.
- [Game client integration](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-integrate-game-client/SKILL.md): Game-specific connection and recovery workflow.
