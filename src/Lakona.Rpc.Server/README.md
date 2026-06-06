# Lakona.Rpc.Server

Server runtime implementation for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Server
```

## Documentation

API reference: https://bruce48x.github.io/Lakona.Rpc/reference/api/

Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/

## Dependencies

- `Lakona.Rpc.Core`

`Lakona.Rpc.Server` has no hard dependency on concrete serializer or transport implementations.

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
    .UseAcceptor(new TcpConnectionAcceptor(20000));

await builder.RunAsync();
```

When the entry assembly contains code-generated `AllServicesBinder`, the builder binds it automatically.

## Extension Boundary

Server applications should not create `RpcSession` directly or hand-write `(serviceId, methodId)` handler dictionaries. `RpcSession`, `RpcServiceRegistry`, and low-level handler delegates are runtime implementation and generated-binder support APIs.

Custom transports and serializers are supported extension points. Implement `ITransport`, `IRpcConnectionAcceptor`, or `IRpcSerializer`, then pass those implementations into `RpcServerHostBuilder`.

## KeepAlive

`RpcServerHostBuilder.UseKeepAlive(...)` enables connection-level idle timeout handling for accepted sessions.

- The server automatically replies to client keepalive pings with pong.
- When enabled on the host, each accepted connection also tracks idle time and disconnects sessions that remain inactive longer than the configured timeout.

## Authentication And Authorization Boundary

`Lakona.Rpc.Server` is focused on RPC session management, transport integration, request dispatch, and connection-level concerns such as framing, keepalive, and transport security.
Request-level authorization is not built into the server runtime by design.

See the canonical design boundary page for the production integration boundary:

- https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/
