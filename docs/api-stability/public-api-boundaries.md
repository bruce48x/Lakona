# Public API Commitment Boundaries

Decision date: 2026-06-05
Last reviewed: 2026-07-22

## Decision

Lakona.Rpc will support third-party transports and serializers as long-term extension points.

Lakona.Rpc will not support user-authored server hosts or direct user
construction of `RpcSession` as a normal extension model. RPC-only server
applications should use `RpcServerHostBuilder`. Lakona.Game server applications
should use `LakonaGameServer.RunAsync(args, configure)` and
`LakonaGameServerBuilder`. Both routes rely on generated service binders,
service implementation classes, and notification contracts.

This distinction keeps the ecosystem open where official packages are necessarily limited, while avoiding a broad commitment to low-level server runtime internals.

## Rationale

Official transport and serializer packages cannot cover every project requirement. Projects may need custom protocols, gateways, compression/encryption stacks, platform-specific networking, or serializer choices. The boundary between runtime and transport/serializer extensions must therefore stay public and stable.

Server session orchestration is different. `RpcSession` owns receive loops, dispatch, keepalive, request pressure, scoped service caches, notification sending, and shutdown behavior for one accepted connection. Exposing it as a user-level host API encourages applications to bypass generated binders and makes future runtime improvements much harder.

The long-term user model should be:

- define shared contracts
- implement service classes
- choose the framework-level composition root:
  - `LakonaGameServer.RunAsync(args, configure)` for Lakona.Game servers
  - `RpcServerHostBuilder` for RPC-only servers
- let generated binders connect contracts to runtime dispatch

Users should not hand-write session loops or `(serviceId, methodId)` handler dictionaries.

## API Layers

The current `0.x` release line has not reached a hard API freeze. In this
document, **stable** identifies the intended long-term commitment boundary and
the preferred supported surface; it does not yet promise that every signature
will remain unchanged. Before a hard freeze, deliberate breaking changes may
still be made under the repository's engineering and release-version rules.

### Stable User API

Regular application projects should build against this layer.

- RPC contract attributes:
  - `RpcServiceAttribute`
  - `RpcMethodAttribute`
  - `RpcNotificationContractAttribute`
  - `RpcNotificationAttribute`
  - `LakonaRpcGenerateClientAttribute`
- Generated client facade shape and lifetime semantics.
- `RpcClientOptions` (intentionally unsealed; `LakonaGameClientOptions` is the
  supported game-layer subclass).
- `RpcClientRuntime` when used through generated clients or advanced client wiring.
- `RpcServerHostBuilder` high-level host configuration.
- `RpcServerHost`.
- `RpcServerLimits`.
- `RpcConnectionInfo` when a generated service factory needs connection
  identity or optional remote endpoint metadata.
- Official transport constructors.
- Official serializer constructors, including:
  - `Lakona.Rpc.Serializer.MemoryPack.MemoryPackRpcSerializer()`
  - `Lakona.Rpc.Serializer.MemoryPack.MemoryPackRpcSerializer(MemoryPackSerializerOptions options)`
- `RpcKeepAliveOptions`.
- `RpcException`.
- `RpcStatus` as framework-only status taxonomy.
- `LakonaGameServer.RunAsync(args, configure)` and
  `LakonaGameServerBuilder.UseClusterRpc(...)`.
- Official cluster adapters:
  - `TcpClusterRpcTransport.Default`
  - `JsonClusterRpcSerializer.Default`
  - `MemoryPackClusterRpcSerializer.Default`

Generated formatter class names under `.Generated` namespaces are not public
API. They may change as formatter generation changes; application code should
use package-level public adapters such as `MemoryPackClusterRpcSerializer`
instead of invoking generated registration types directly.

### Stable Extension API

Extension authors can rely on this layer for custom transports, serializers, and connection acceptors.

- `ITransport`.
- `IRpcSerializer`.
- `IRpcConnectionAcceptor`.
- `IRemoteEndPointProvider`.
- `RpcAcceptedConnection`.
- `TransportFrame`.
- `IClusterRpcTransport` for paired outbound connection and inbound listener
  behavior.
- `IClusterRpcSerializer` for a stable cluster protocol ID and serializer
  construction.

This layer should have contract tests and clear documentation because third-party packages will compile against it directly.

### Generated-Support API

Generated code uses this layer. Users may see it, but compatibility is tied to matching runtime and analyzer package versions.

- `IRpcClient`.
- `RpcMethod<TArg, TResult>`.
- `RpcNotificationMethod<TArg>`.
- `RpcClientRuntime.CallRawAsync(...)` and
  `RpcClientRuntime.RegisterRawNotificationHandler(...)` only for
  framework-owned generated or Lakona.Game control paths.
- `RpcGeneratedServicesBinderAttribute`.
- `RpcGeneratedServiceBinder`.
- `RpcServiceRegistry`, until the generator no longer exposes registry binding directly.
- `RpcServiceRegistration<TService>` and `RpcNotificationChannel` as hidden
  generated/runtime cooperation types.
- `RpcRawHandler` and `RpcRawResult` for framework-owned control protocols.

Breaking changes in this layer must be released together with analyzer changes and must tell users to rebuild source-generated code.

The raw `RpcClientRuntime` methods are public because generated client code may
live in user assemblies, but they are hidden from normal IntelliSense and are
not the recommended business RPC model. User-authored business calls should go
through generated typed clients and configured serializers.

### Runtime Internal API

This layer is assembly-internal and is not a user extension surface.

- `RpcSession`.
- `RpcHandler`.
- `RpcSessionHandler`.
- Direct `(serviceId, methodId)` handler registration.
- `RpcSession.GetOrAddScopedService`.
- Low-level `RpcSession.SendNotificationAsync(serviceId, methodId, payload)`.

Generated code and framework binders must not reference these types.

### Protocol and Infrastructure API

This layer supports protocol tools, tests, diagnostics, and package-internal cooperation. It is not a business application entry point.

- `RpcEnvelopeCodec`.
- `RpcFrameType`.
- `RpcRequestEnvelope`.
- `RpcResponseEnvelope`.
- `RpcPushEnvelope`.
- `RpcRequestFrame`.
- `RpcResponseFrame`.
- `RpcPushFrame`.
- `RpcKeepAlivePingEnvelope`.
- `RpcKeepAlivePongEnvelope`.
- `RpcProtocolLimits`.
- `LengthPrefix`.
- `TransportFrameCodec`.
- `TransformingTransport`.
- `TransportSecurityConfig`.
- `PooledFrameBufferWriter`.

Some of these may remain public, especially when protocol testing or transport implementation requires them. Others should be evaluated for `internal` visibility or `EditorBrowsable(Never)`.

## Removed Boundary Leak

Older generated server binders exposed `RpcSession` in public generated signatures such as:

```csharp
BindFactory(RpcServiceRegistry registry, Func<RpcSession, TService> implFactory)
```

Generated notification proxies also wrapped `RpcSession` to call:

```csharp
RpcSession.SendNotificationAsync(serviceId, methodId, payload)
```

This leak was removed on 2026-07-22. Generated binders now accept
`RpcConnectionInfo`; generated callback proxies use `RpcNotificationChannel`.
`RpcSession`, its low-level handlers, and direct handler registration are
assembly-internal.

## Target Replacement

The replacement boundary uses an immutable connection identity value:

```csharp
public sealed class RpcConnectionInfo
{
    string ConnectionId { get; }
    EndPoint? RemoteEndPoint { get; }
}
```

`ConnectionId` is generated by `RpcServerHost` and is independent of transport
`DisplayName`. `RemoteEndPoint` is optional transport metadata and may be null.
Generated service factories receive this value only when service construction
needs connection information. Notification support continues to be exposed to
business code through generated notification contracts; the generated proxy
uses the hidden `RpcNotificationChannel` support type instead of `RpcSession`.

The typed `RpcServiceRegistration<TService>` seam owns payload serialization,
connection-scoped activation, invocation, and response encoding. Framework
control protocols that intentionally use their own codec use `RpcRawHandler`
and return `RpcRawResult`; neither seam exposes the runtime serializer,
transport frame writer, receive loop, or service cache.

## Current State

User-facing package documentation uses high-level host entry points rather than
teaching direct `RpcSession` construction as the normal server path.
Runtime-internal types that remain public for generated code have XML remarks
and `EditorBrowsable(EditorBrowsableState.Never)`. The stable extension
interfaces remain public, documented, and covered by focused runtime and host
integration tests. `RpcServerHost` assigns opaque connection ids, registrations
reject duplicate method ids, and connection-scoped activation uses
single-publication semantics. Factory-created services are released after
in-flight requests drain; explicitly bound singleton instances remain
caller-owned.

The RPC source generator, Game control RPCs, Hotfix generator, Cluster binders,
and maintained mixed-transport sample all use the typed/raw boundary.
`RpcSession` and the legacy handler path are assembly-internal.

## Non-Goals

This decision does not remove support for custom transports, serializers, or connection acceptors.

This decision does not remove generated service binding.

This decision does not commit to a full dependency injection container abstraction. Service construction should stay simple until a concrete need appears.

## Release and Documentation Rules

- RPC-only tutorials should use `RpcServerHostBuilder`; Lakona.Game tutorials
  should use `LakonaGameServer.RunAsync(args, configure)`. Both should rely on
  generated binders.
- Package READMEs should not teach direct `RpcSession` construction as the normal server path.
- API reference entries for runtime-internal types should warn that they are not user extension points.
- Breaking changes in generated-support APIs must mention analyzer/runtime version coupling.
- Stable extension APIs must receive focused tests throughout the `0.x` release
  line and before a hard freeze.
- A hard freeze must be declared explicitly in current authority and release
  documentation; it is not implied by a **stable** layer heading.
