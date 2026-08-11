# Lakona.Rpc.Server

Server runtime implementation for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Server
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Dependencies

- `Lakona.Rpc.Core`
- `Microsoft.Extensions.Logging.Abstractions`

`Lakona.Rpc.Server` has no hard dependency on concrete serializer, transport,
or logging-provider implementations.

## Includes

- `RpcServerHostBuilder`
- `RpcServerHost`
- `RpcGeneratedServiceBinder`
- runtime dispatch infrastructure used by generated service binders

## Recommended Usage

Use `RpcServerHostBuilder` to compose serializer, transport, generated binders, and security in one place:

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
    .UseLimits(limits => limits.MaxActiveConnections = 10000)
    .UseAcceptor(new TcpConnectionAcceptor(20000));

await builder.RunAsync();
```

Pass an application-owned `ILoggerFactory` through `UseLoggerFactory` when
logging is required. The runtime uses a null logger when no factory is supplied.

When the entry assembly contains code-generated `AllServicesBinder`, the builder binds it automatically.

`MaxActiveConnections` is a hard host limit. A newly accepted transport is
closed before Session construction when the budget is full. Framework
integrations can additionally use `IRpcSessionAdmissionGate` for protocol-level
admission deadlines; application authorization remains in generated services.
The host invokes `IRpcSessionLifecycleObserver.OnSessionDisconnectedAsync`
only after Session resources and admission leases are released and the active
connection slot is returned.

## Extension Boundary

Server applications should not hand-write session loops or `(serviceId, methodId)` handler dictionaries. `RpcSession` and low-level handler delegates are runtime-internal; `RpcServiceRegistry` is generated-binder support API.

Custom transports and serializers are supported extension points. Implement `ITransport`, `IRpcConnectionAcceptor`, or `IRpcSerializer`, then pass those implementations into `RpcServerHostBuilder`.

## KeepAlive

`RpcServerHostBuilder.UseKeepAlive(...)` enables connection-level idle timeout handling for accepted sessions.

- The server automatically replies to client keepalive pings with pong.
- When enabled on the host, each accepted connection also tracks idle time and disconnects sessions that remain inactive longer than the configured timeout.

## Authentication And Authorization Boundary

`Lakona.Rpc.Server` is focused on RPC session management, transport integration, request dispatch, and connection-level concerns such as framing, keepalive, and transport security.
Request-level authorization is not built into the server runtime by design.

See the canonical design boundary page for the production integration boundary:

- https://bruce48x.github.io/Lakona/concepts/design-boundary/
