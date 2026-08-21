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

Cancellation starts a cooperative Session drain under one host-wide shutdown
deadline (15 seconds by default). If a handler ignores cancellation past that
deadline, the host aborts active transports and throws
`RpcServerShutdownTimeoutException`. It does not dispose Session-scoped state
concurrently with code that may still be using it; the application composition
root should treat this failure as terminal and decide how the process exits.
The forced transport-abort join is itself bounded by the configured timeout.
The forced transport-abort join is itself bounded by the configured timeout.

Pass an application-owned `ILoggerFactory` through `UseLoggerFactory` when
logging is required. The runtime uses a null logger when no factory is supplied.
See [Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md) for
provider and lifetime guidance.

Request count, response status, and dispatch duration are emitted through the
standard `Lakona.Rpc.Server` .NET `Meter`. Use
`LakonaRpcServerTelemetry.MeterName` when configuring an OpenTelemetry metrics
pipeline; the runtime does not own an exporter.

When the entry assembly contains code-generated `AllServicesBinder`, the builder binds it automatically.

`MaxActiveConnections` is a hard host limit. A newly accepted transport is
closed before Session construction when the budget is full. Framework
integrations can additionally use `IRpcSessionAdmissionGate` for protocol-level
admission deadlines; application authorization remains in generated services.
An `IRpcSessionRequestGate` returns an explicit denial for expected policy
outcomes. If a gate throws unexpectedly, the server logs the root cause,
returns a sanitized `InternalError`, and keeps the RPC Session usable.
The host invokes `IRpcSessionLifecycleObserver.OnSessionDisconnectedAsync`
only after Session resources and admission leases are released and the active
connection slot is returned.

`MaxConcurrentRequestsPerSession` plus `MaxQueuedRequestsPerSession` form the
finite per-Session request budget. When it is full, the receive loop sends one
`Overloaded` response before reading the next application frame, so a stalled
response path propagates transport backpressure instead of accumulating
unbounded rejection tasks.

## Extension Boundary

Server applications should not hand-write session loops or `(serviceId, methodId)` handler dictionaries. `RpcSession` and low-level handler delegates are runtime-internal; `RpcServiceRegistry` is generated-binder support API.

Custom transports and serializers are supported extension points. Implement `ITransport`, `IRpcConnectionAcceptor`, or `IRpcSerializer`, then pass those implementations into `RpcServerHostBuilder`.

## KeepAlive

`RpcServerHostBuilder.UseKeepAlive(...)` enables connection-level idle timeout handling for accepted sessions.

- The server automatically replies to client keepalive pings with pong.
- When enabled on the host, each accepted connection also tracks idle time and disconnects sessions that remain inactive longer than the configured timeout.
- Session completion joins keepalive work before scoped services or the owned transport are released; an unexpected keepalive failure is reported as the Session disconnect reason.

## Authentication And Authorization Boundary

`Lakona.Rpc.Server` is focused on RPC session management, transport integration, request dispatch, and connection-level concerns such as framing, keepalive, and transport security.
Request-level authorization is not built into the server runtime by design.

See the canonical design boundary page for the production integration boundary:

- https://bruce48x.github.io/Lakona/concepts/design-boundary/
