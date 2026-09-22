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
    .UseShutdownTimeout(TimeSpan.FromSeconds(15))
    .UseAcceptor(new TcpConnectionAcceptor(20000));

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

`RpcServerHost` is token-driven and does not subscribe to process signals.
Standalone applications own Ctrl+C or service-lifetime integration at their
composition root, as above. Embedded hosts pass their existing shutdown token.

Use `UseShutdownTimeout` to bound cooperative shutdown and handle
`RpcServerShutdownTimeoutException` at the application boundary. Cleanup order,
timeout behavior, and admission ownership are defined in
[Host And Session Lifetime](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md#host-and-session-lifetime).

Pass an application-owned `ILoggerFactory` through `UseLoggerFactory` when
logging is required. The runtime uses a null logger when no factory is supplied.
See [Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md) for
provider and lifetime guidance.

Use `LakonaRpcServerTelemetry.MeterName` when configuring an OpenTelemetry metrics
pipeline; see
[Observability](https://github.com/bruce48x/Lakona/blob/main/docs/observability.md)
for metric and exporter guidance.

When the entry assembly contains code-generated `AllServicesBinder`, the builder binds it automatically.

Configure `MaxActiveConnections`, `MaxConcurrentRequestsPerSession`, and
`MaxQueuedRequestsPerSession` through `UseLimits` for connection and request
budgets. Their backpressure behavior follows
[Host And Session Lifetime](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md#host-and-session-lifetime).
Framework integrations can use admission gates and lifecycle observers through
the [Framework Integration API](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/public-api-boundaries.md#framework-integration-api).
Gate denials and failures follow the
[Status and Error Model](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/status-error-model.md).

## Extension Boundary

Server applications should not hand-write session loops or `(serviceId, methodId)` handler dictionaries. `RpcSession` and low-level handler delegates are runtime-internal; `RpcServiceRegistry` is generated-binder support API.

Custom transports and serializers are supported extension points. Implement `ITransport`, `IRpcConnectionAcceptor`, or `IRpcSerializer`, then pass those implementations into `RpcServerHostBuilder`.

## KeepAlive

`RpcServerHostBuilder.UseKeepAlive(...)` enables connection-level idle timeout handling for accepted sessions.

- The server automatically replies to client keepalive pings with pong.
- When enabled on the host, each accepted connection also tracks idle time and disconnects sessions that remain inactive longer than the configured timeout.

Keepalive cleanup and failure propagation follow the
[Session lifetime contract](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/architecture.md#host-and-session-lifetime).

## Authentication And Authorization Boundary

`Lakona.Rpc.Server` is focused on RPC session management, transport integration, request dispatch, and connection-level concerns such as framing, keepalive, and transport security.
Request-level authorization is not built into the server runtime by design.

See the canonical design boundary page for the production integration boundary:

- https://bruce48x.github.io/Lakona/concepts/design-boundary/
