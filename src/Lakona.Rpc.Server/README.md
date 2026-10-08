# Lakona.Rpc.Server

RPC hosting, generated service binding, and request dispatch. Compose the host
with your selected transport, serializer, and application lifetime. Game
applications normally use `Lakona.Game.Server` for framework hosting.

## Install

```bash
dotnet add package Lakona.Rpc.Server
```

## Minimal Host

```csharp
await RpcServerHostBuilder.Create()
    .UseSerializer(serializer)
    .UseAcceptor(acceptor)
    .RunAsync(shutdownToken);
```

Import `Lakona.Rpc.Server`; supply a configured serializer, acceptor, and an
application-owned shutdown token.

## Guides

- [RPC architecture](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md): host/session lifetime, limits, KeepAlive, dispatch, and shutdown.
- [Public API boundaries](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/public-api-boundaries.md): service binding and framework admission extensions.
- [Source generation](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/source-generation.md): generated binders and contract discovery.
- [Status model](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/status-error-model.md): admission and failure handling.
- [Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md), [Observability](https://github.com/bruce48x/Lakona/blob/main/docs/observability.md): application-owned diagnostics setup.
- [Implement Game service](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-service/SKILL.md): Hotfix service implementation workflow.
