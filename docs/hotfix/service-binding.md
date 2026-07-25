# Generated Hotfix Service Binding

This document describes how generated Lakona projects bind stable RPC and
Application HTTP service contracts to hotfix-backed server logic.

For the hotfix loading model, dispatch publication, `BuildTag`, development
reload, and production activation, see [Hotfix Architecture](architecture.md).
For game session identity, connection binding, callback resolution, disconnect, resume, and
termination semantics, see [Session Lifecycle](../session.md).

## Purpose

Generated Lakona projects should not require users to hand-write stable RPC
service proxies, ASP.NET endpoint registration, binder configuration, service
endpoint marker files, or raw `RpcSession` lifecycle subscriptions when they
add a new service.

The default model is:

```txt
Shared RPC contract
  -> generated stable Server.App binding
  -> current Server.Hotfix service method
  -> framework-owned session lifecycle APIs when needed

Stable Application HTTP contract
  -> generated stable ASP.NET endpoint registration
  -> generation-pinned LakonaHttpCall
  -> current Server.Hotfix service method
```

## Decisions

- Shared contracts remain the source of truth for service, callback, and DTO
  shape.
- Stable Application HTTP contracts remain the source of truth for service
  name, method, route, request/response boundary, and numeric method id.
- Generated stable server code binds hotfix-backed service implementations
  without requiring user-authored service proxies, endpoint marker files, or
  binder configuration.
- `EndpointName` and `GameEndpointName` are not user-facing concepts in
  generated service binding.
- Framework session identity stays server-side. Generated shared RPC DTOs,
  generated client code, and MemoryPack formatters must not expose, serialize,
  store, or echo it.
- Raw `RpcSession` lifecycle subscriptions are not part of generated service
  binding. Session lifecycle behavior belongs to the framework APIs and hooks
  described in [Session Lifecycle](../session.md).

## Generated Binding

Shared contracts stay the single source of truth:

```csharp
[RpcService(RpcContractIds.Services.Chat, NotificationContract = typeof(IChatCallback))]
public interface IChatService
{
    [RpcMethod(RpcContractIds.ChatServiceMethods.BindAsync)]
    ValueTask BindAsync(ChatBindRequest req);

    [RpcMethod(RpcContractIds.ChatServiceMethods.SendAsync)]
    ValueTask SendAsync(ChatSendRequest req);
}
```

The stable server project should not contain a service endpoint marker file or
hand-written service proxy files. There should be no user-authored equivalent
of:

```txt
Server/App/Services/GeneratedServiceEndpoints.cs
Server/App/Hosting/ServiceBindingConfigurator.cs
Server/App/Chat/ChatServiceProxy.cs
```

`Program.cs` is a thin infrastructure composition root. It registers only the
transport and serializer implementations selected during project generation:

```csharp
using Lakona.Game.Cluster.Rpc.Serializer.MemoryPack;
using Lakona.Game.Cluster.Rpc.Transport.Tcp;
using Lakona.Game.Server.Hosting;

return await LakonaGameServer.RunAsync(args, static server => server
    .UseClusterRpc(TcpClusterRpcTransport.Default, MemoryPackClusterRpcSerializer.Default)
    .RegisterEndpointTransport("kcp", static endpoint => new KcpConnectionAcceptor(endpoint.Port, endpoint.Host))
    .RegisterEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer()));
```

Client endpoint transport and serializer names live in configuration;
generated `Program.cs` binds those names and selects one code-level cluster
transport/serializer pair from explicitly referenced implementations. It must not
contain business services, actor startup, or generated RPC binding calls.
Generated hotfix-backed RPC services are selected by endpoint-local
`RpcServices`. The generator emits `LakonaRpcServiceBinder` adapters and
`IHotfixRequiredServiceContracts` providers; `LakonaGameServer.RunAsync`
discovers both automatically from the application assembly.

Stable service proxy generation is app-side only. The same hotfix generator
package may run in both `Server.App` and `Server.Hotfix`, but
`Server.Hotfix` must not emit another `*ServiceProxy`, endpoint binder, or
`IHotfixRequiredServiceContracts` provider for shared RPC contracts. Hotfix
projects compile the replaceable `[HotfixService]` and `[HotfixLifecycle]`
implementations plus behavior-derived actor refs and generic call helpers;
stable RPC binding remains in `Server.App`.

There is no user-authored `.UseGeneratedHotfixServices()` step in generated
projects. Generated binders and required-contract providers are framework
discovery artifacts, not fluent host calls that users copy into `Program.cs`.

When a user adds a new shared `[RpcService]` interface and implements a matching
hotfix `[HotfixService]`, no stable proxy file, binding configurator, endpoint
marker, or endpoint name should be written by hand.

### Application HTTP Binding

Stable `Server.App` contracts use `[LakonaHttpService]` and
`[LakonaHttpEndpoint]`:

```csharp
[LakonaHttpService("operations")]
public interface IOperationsHttpService
{
    [LakonaHttpEndpoint(301, "POST", "/operations/player/inspect")]
    ValueTask<LakonaHttpResponse> InspectAsync(LakonaHttpRequest request);
}
```

The generator emits the endpoint registration and an
`IHotfixRequiredServiceContracts` provider. A configured
`Lakona:Http:Listeners[]:Services` entry selects the service name on a physical
listener. Hotfix implements the contract through the corresponding
`LakonaHttpCall` shape:

```csharp
[HotfixService(typeof(IOperationsHttpService))]
public sealed class OperationsHttpService
{
    public ValueTask<LakonaHttpResponse> InspectAsync(LakonaHttpCall call)
    {
        return new ValueTask<LakonaHttpResponse>(
            LakonaHttpResponse.Json(new { traceId = call.Request.TraceIdentifier }));
    }
}
```

`LakonaHttpCall` exposes the bounded stable request snapshot, cancellation
token, generation provider, Actor runtime, and game-server API. It deliberately
does not expose `HttpContext`. One admitted request holds one Hotfix runtime
lease through response materialization; a reload affects the next request, not
an already executing request. The snapshot is detached from `HttpContext` but
is not a deep hostile-code immutability boundary; handlers treat its
request-owned buffers and collections as read-only and must observe the
cooperative cancellation token.

Candidate validation checks every generated HTTP contract method, not only the
presence of an implementation class. The Hotfix method must match the stable
method id, method name, logical request type through `LakonaHttpCall`, and exact
`ValueTask<LakonaHttpResponse>` return type. A missing method or mismatched
return type prevents candidate publication.

### Hotfix Service Dependencies

Hotfix service implementations express dependencies through constructors:

```csharp
[HotfixService(typeof(IChatService))]
public sealed class ChatService
{
    private readonly ChatPresenceStore _presence;

    public ChatService(ChatPresenceStore presence)
    {
        _presence = presence;
    }

    public ValueTask SendAsync(ChatServiceCall<ChatSendRequest> call)
    {
        // Use _presence for dependencies and call for request-specific data.
        return default;
    }
}
```

`ChatServiceCall<TRequest>` is generated in the stable server assembly from
the shared `IChatService` contract. Its `Callback` property is statically typed
as `IChatCallback`, so hotfix handlers do not repeat the service callback type
on every method. Services without a notification contract receive the same
service-scoped call shape without a `Callback` property. All generated call
types expose the request, connection id, current Game Session and item snapshot,
hotfix services, Actors, and game-server APIs.

The framework-level `HotfixServiceCall<TRequest>` and
`HotfixServiceCall<TRequest, TCallback>` structs remain dispatch support types.
Generated projects should author handlers with the service-scoped generated
call type instead.

The hotfix startup method marked `[HotfixConfigureServices]` registers
dependencies used by hotfix logic. It does not register `[HotfixService]`
classes themselves. The dispatch layer owns service implementation lifetime and
creates one instance per published hotfix generation.

Constructor parameters resolve from the current hotfix generation provider
first and the stable root provider second. Generation-local dependencies should
be registered through the `[HotfixConfigureServices]` startup method; stable
framework dependencies should come from the root provider and must not capture
`Server.Hotfix` types.

Non-static `[HotfixService]` and `[HotfixLifecycle]` implementation classes
must have one public constructor, or one public constructor marked with
`[ActivatorUtilitiesConstructor]`. Missing dependencies, open generic
implementations, multiple unmarked public constructors, and activation failures
fail hotfix validation or reload before the candidate generation is published.

The dispatch layer owns each non-static service or lifecycle instance for the
entire published generation and disposes it only after that generation retires
and its in-flight calls drain. Service instances are therefore concurrent
coordinators: constructors may capture generation-scoped dependencies, but
request state belongs in the readonly `HotfixServiceCall` value, durable mutable
state belongs in Actors or Game Sessions, and any mutable coordinator field
must be synchronized explicitly. Constructors must not start unmanaged
background work or subscriptions that outlive generation disposal.

Generated dispatch uses stable numeric method ids and cached typed delegates.
Warm service and Actor calls must not construct method-name keys, type arrays,
argument arrays, or invoke user methods through reflection. Static service
methods remain supported for stateless helpers, but are no longer required to
avoid one implementation allocation per request.

## Session Lifecycle Boundary

Generated binding code may call session-oriented game server APIs when a
service needs to start a game session, publish callbacks, or
terminate a session. This document does not define those lifecycle contracts.

The boundary is:

- Generated binding code may depend on framework-owned session APIs.
- Generated binding code must not require endpoint names.
- Generated shared contracts must not expose `GameSessionKey`.
- Session disconnect, expiration, termination, and resume semantics belong in
  [Session Lifecycle](../session.md).

## Generated Project Shape

Generated docs should teach three edit zones:

- `Shared/Contracts/**`: define client/server RPC contracts, callback
  contracts, DTOs, and stable numeric ids.
- `Server/App/**`: keep the executable host, stable actor state, and generated
  RPC binding.
- `Server/Hotfix/**`: implement hot-reloadable services, user-authored
  `*Lifecycle` classes such as `ChatSessionLifecycle`, and actor behavior
  logic.

Generated projects must not teach users to put presence cleanup, matchmaking
cleanup, room leave policy, or session business policy in `Server.App`.
When framework runtime code observes a lifecycle event, the framework-owned
bridge forwards the event to a hotfix lifecycle contract through
`IHotfixServiceInvoker` and a numeric method id. Do not name user lifecycle
classes `*LifecycleService`.

There should be no generated-project edit zone for service endpoint markers,
stable service proxies, raw RPC lifecycle subscriptions, App lifecycle bridge
classes, or App lifecycle runtime contract files.

## Validation Requirements

Tests and source scans should reject these patterns in generated projects:

```txt
class LoginServiceProxy
class ChatServiceProxy
ServiceBindingConfigurator
GeneratedServiceEndpoints
HotfixRpcService(
EndpointName
GameEndpointName
RpcSession.Disconnected +=
```

Allow those names only in tests that intentionally cover removed API behavior
or in explicitly historical release notes.
