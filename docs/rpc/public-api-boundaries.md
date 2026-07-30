# Lakona.Rpc Public API Boundaries

Last reviewed: 2026-07-30

## Commitment

Lakona.Rpc supports third-party transports, serializers, and connection
acceptors as extension points.

Lakona.Rpc does not support user-authored server hosts or direct user
construction of `RpcSession` as a normal extension model. RPC-only server
applications should use `RpcServerHostBuilder`. Lakona.Game server applications
should use `LakonaGameServer.RunAsync(args, configure)` and
`LakonaGameServerBuilder`. Both routes rely on generated service binders,
service implementation classes, and notification contracts.

This distinction keeps the ecosystem open where official packages are
necessarily limited without exposing low-level server runtime internals.

## Rationale

Official transport and serializer packages cannot cover every project
requirement. Projects may need custom protocols, gateways,
compression/encryption stacks, platform-specific networking, or serializer
choices. The transport and serializer contracts must therefore stay public.

Server session orchestration is different. `RpcSession` owns receive loops,
dispatch, keepalive, request pressure, scoped service caches, notification
sending, and shutdown behavior for one accepted connection. Exposing it as a
user-level host API would let applications bypass generated binders and couple
them to runtime implementation details.

The supported user model is:

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
  - `LakonaGameGenerateClientAttribute`, with its parameterless constructor;
    runtime, platform, and game-version metadata belong to project configuration
    and are not attribute arguments.
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
- `RpcUnhandledNotificationContext` and
  `RpcNotificationHandlerExceptionContext` through `RpcClientRuntime` events.
- `LakonaGameServer.RunAsync(args, configure)`. The server owns its fixed TCP +
  MemoryPack cluster channel; the builder configures only client-facing
  endpoints and application services. Cluster composition is defined in
  [Cluster](../cluster.md#cluster-rpc-composition).

### Stable Extension API

Extension authors can rely on this layer for custom transports, serializers, and connection acceptors.

- `ITransport`.
- `IRpcSerializer`, whose `Serialize<T>(IBufferWriter<byte>, T)` implementation
  writes only payload bytes synchronously and neither owns nor retains the
  supplied writer.
- `IRpcConnectionAcceptor`.
- `IRemoteEndPointProvider`.
- `RpcAcceptedConnection`.
- `RpcConnectionAdmissionDefaults`.
- `TransportFrame`.
- `RpcSerializerExtensions.SerializeFrame<T>` for extension authors that
  explicitly need a standalone owned payload frame outside the normal runtime
  envelope path.

This layer should have contract tests and clear documentation because third-party packages will compile against it directly.

### Framework Integration API

Higher-level frameworks may observe connection lifecycle and gate requests
through the host without accessing `RpcSession`.

- `IRpcServerLifecycleObserver` and `RpcServerListeningContext`.
- `IRpcSessionLifecycleObserver` and `RpcSessionLifecycleContext`.
- `IRpcSessionRequestGate`, `RpcSessionRequestGateContext`, and
  `RpcSessionRequestGateResult`.

The server lifecycle observer receives listener readiness only. Session hooks
receive connection identity and request metadata. Business services should
continue to use generated contracts and binders.

### Runtime Package Cooperation API

The separately published client and server runtimes cooperate through one
Core-owned connection module instead of receiving friend-assembly access to
Core internals.

- `RpcConnectionChannel`.

The channel serializes transport writes, tracks send and receive activity,
consumes keepalive ping/pong frames, measures RTT, and runs peer-liveness
probing. It is hidden from normal IntelliSense and is not an application
extension point. Client and server receive only application frames from it.

### Generated-Support API

Generated code uses this layer. Users may see it, but compatibility is tied to
the runtime package version. `Lakona.Rpc.Core` carries the matching analyzer
assembly so consumers cannot select an incompatible analyzer package.

- `IRpcClient`.
- `RpcMethod<TArg, TResult>`.
- `RpcNotificationMethod<TArg>`.
- `RpcVoid`.
- `IRpcNotificationDispatchTarget`, `RpcNotificationPayloadHandler`, and
  `RpcNotificationDispatchMiddleware` for generated or framework-owned
  notification dispatch.
- `RpcClientRuntime.CallRawAsync(...)` and
  `RpcClientRuntime.RegisterRawNotificationHandler(...)` only for
  framework-owned generated or Lakona.Game control paths.
- `RpcGeneratedServicesBinderAttribute`.
- `RpcGeneratedServiceBinder`.
- `RpcServiceRegistry` and `RpcMethodDescriptor`.
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
- `RpcPushMetadata`.
- `RpcRequestFrame`.
- `RpcResponseFrame`.
- `RpcPushFrame`.
- `RpcKeepAlivePingEnvelope`.
- `RpcKeepAlivePongEnvelope`.
- `RpcProtocolLimits`.
- `LengthPrefix`.
- `LengthPrefixedFrameAccumulator`.
- `TransportFrameCodec`.
- `TransformingTransport`.
- `TransportSecurityConfig`.
- `PooledFrameBufferWriter`.
- `RpcEnvelopePayloadWriter`.

These types remain public for protocol testing, transport implementation, and
package-internal cooperation. They are not business application APIs.

Official and third-party transports use the same protocol infrastructure
interface. Core does not grant privileged internal access to official
transport assemblies.

## Runtime Cooperation Contract

`RpcConnectionInfo` carries an opaque host-assigned `ConnectionId` and optional
remote endpoint metadata. Generated service factories receive it only when
service construction needs connection information.

Generated notification proxies use the hidden `RpcNotificationChannel` support
type. Business code continues to publish through generated notification
contracts rather than sending numeric method ids directly.

`RpcServiceRegistration<TService>` owns payload serialization,
connection-scoped activation, invocation, and response encoding. Typed client
requests, typed server responses, and typed server notifications serialize
directly into a Core-owned final envelope writer; serializers receive only its
payload region and do not own or retain the writer. Framework control protocols
that own their codec use `RpcRawHandler` and `RpcRawResult`. Neither path
exposes the runtime serializer, transport frame writer, receive loop, or
service cache to business code.

Registrations reject duplicate method ids. Connection-scoped activation uses
single-publication semantics. Factory-created services are released after
in-flight requests drain; explicitly bound singleton instances remain
caller-owned.

## Non-Goals

- Removing custom transports, serializers, or connection acceptors.
- Removing generated service binding.
- Adding a dependency injection container abstraction to RPC service
  construction without a concrete requirement.

## Release and Documentation Rules

- RPC-only tutorials should use `RpcServerHostBuilder`; Lakona.Game tutorials
  should use `LakonaGameServer.RunAsync(args, configure)`. Both should rely on
  generated binders.
- Package READMEs should not teach direct `RpcSession` construction as the
  normal server path.
- API reference entries for runtime-internal types should warn that they are not
  user extension points.
- Breaking changes in generated-support APIs must mention analyzer/runtime
  version coupling.
- Stable extension APIs must receive focused tests throughout the `0.x` release
  line and before a hard freeze.
- A hard freeze must be declared explicitly in current authority and release
  documentation; it is not implied by a **stable** layer heading.
