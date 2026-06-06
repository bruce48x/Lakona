# Public API Commitment Boundaries

Date: 2026-06-05

## Decision

Lakona.Rpc will support third-party transports and serializers as long-term extension points.

Lakona.Rpc will not support user-authored server hosts or direct user construction of `RpcSession` as a normal extension model. Server applications should use `RpcServerHostBuilder`, generated service binders, service implementation classes, and notification contracts.

This distinction keeps the ecosystem open where official packages are necessarily limited, while avoiding a broad commitment to low-level server runtime internals.

## Rationale

Official transport and serializer packages cannot cover every project requirement. Projects may need custom protocols, gateways, compression/encryption stacks, platform-specific networking, or serializer choices. The boundary between runtime and transport/serializer extensions must therefore stay public and stable.

Server session orchestration is different. `RpcSession` owns receive loops, dispatch, keepalive, request pressure, scoped service caches, notification sending, and shutdown behavior for one accepted connection. Exposing it as a user-level host API encourages applications to bypass generated binders and makes future runtime improvements much harder.

The long-term user model should be:

- define shared contracts
- implement service classes
- configure `RpcServerHostBuilder`
- let generated binders connect contracts to runtime dispatch

Users should not hand-write session loops or `(serviceId, methodId)` handler dictionaries.

## API Layers

### Stable User API

Regular application projects can rely on this layer after a hard freeze.

- RPC contract attributes:
  - `RpcServiceAttribute`
  - `RpcMethodAttribute`
  - `RpcNotificationContractAttribute`
  - `RpcNotificationAttribute`
  - `LakonaRpcGenerateClientAttribute`
- Generated client facade shape and lifetime semantics.
- `RpcClientOptions`.
- `RpcClientRuntime` when used through generated clients or advanced client wiring.
- `RpcServerHostBuilder` high-level host configuration.
- `RpcServerHost`.
- `RpcServerLimits`.
- Official transport constructors.
- Official serializer constructors.
- `RpcKeepAliveOptions`.
- `RpcException`.
- `RpcStatus` as framework-only status taxonomy.

### Stable Extension API

Extension authors can rely on this layer for custom transports, serializers, and connection acceptors.

- `ITransport`.
- `IRpcSerializer`.
- `IRpcConnectionAcceptor`.
- `IRemoteEndPointProvider`.
- `RpcAcceptedConnection`.
- `TransportFrame`.

This layer should have contract tests and clear documentation because third-party packages will compile against it directly.

### Generated-Support API

Generated code uses this layer. Users may see it, but compatibility is tied to matching runtime and analyzer package versions.

- `IRpcClient`.
- `RpcMethod<TArg, TResult>`.
- `RpcNotificationMethod<TArg>`.
- `RpcGeneratedServicesBinderAttribute`.
- `RpcGeneratedServiceBinder`.
- `RpcServiceRegistry`, until the generator no longer exposes registry binding directly.

Breaking changes in this layer must be released together with analyzer changes and must tell users to rebuild source-generated code.

### Runtime Internal API

This layer should not be presented as user extension surface. If it remains public temporarily, it should be hidden from normal IntelliSense and documented as implementation support.

- `RpcSession`.
- `RpcHandler`.
- `RpcSessionHandler`.
- Direct `(serviceId, methodId)` handler registration.
- `RpcSession.GetOrAddScopedService`.
- Low-level `RpcSession.SendNotificationAsync(serviceId, methodId, payload)`.

Long-term target: remove these from ordinary public usage. Some may remain public only as generated-support plumbing until generator output is redesigned.

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

## Current Boundary Leak

Generated server binders currently expose `RpcSession` in public generated signatures such as:

```csharp
BindFactory(RpcServiceRegistry registry, Func<RpcSession, TService> implFactory)
```

Generated notification proxies also wrap `RpcSession` internally to call:

```csharp
RpcSession.SendNotificationAsync(serviceId, methodId, payload)
```

This leaks the runtime session object into the generated API surface. It conflicts with the decision that users should not author server hosts or directly depend on `RpcSession`.

## Target Replacement

Introduce a narrower server-side context boundary before making `RpcSession` internal.

Potential shape:

```csharp
public interface IRpcServiceContext
{
    string ContextId { get; }
    string? RemoteEndPoint { get; }
}
```

Generated service factories should accept this narrow context only if service construction needs session metadata. Notification support should continue to be exposed through generated notification contract interfaces, not through `RpcSession`.

The exact context shape should be designed separately before changing generator output.

## Migration Order

1. Document the API layers and commitment boundary.
2. Remove direct `new RpcSession(...)` examples from user-facing server README material.
3. Add XML remarks and `EditorBrowsable(EditorBrowsableState.Never)` to runtime-internal public types that remain public temporarily.
4. Keep `ITransport`, `IRpcSerializer`, `IRpcConnectionAcceptor`, `RpcAcceptedConnection`, and `TransportFrame` documented as stable extension points.
5. Design a narrow server service context to replace public generated signatures that expose `RpcSession`.
6. Change source generator output to stop publicly exposing `RpcSession`.
7. After generated code no longer exposes it, evaluate making `RpcSession` constructors internal or making the type internal.

## Non-Goals

This decision does not remove support for custom transports, serializers, or connection acceptors.

This decision does not remove generated service binding.

This decision does not immediately make `RpcSession` internal. That should happen only after generator output and tests no longer require it as public API.

This decision does not commit to a full dependency injection container abstraction. Service construction should stay simple until a concrete need appears.

## Release and Documentation Rules

- User tutorials should prefer `RpcServerHostBuilder` and generated binders.
- Package READMEs should not teach direct `RpcSession` construction as the normal server path.
- API reference entries for runtime-internal types should warn that they are not user extension points.
- Breaking changes in generated-support APIs must mention analyzer/runtime version coupling.
- Stable extension APIs must receive focused tests before hard freeze.
