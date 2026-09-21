# Generated Hotfix Service Binding

This document describes how generated Lakona projects bind stable RPC and
Application HTTP service contracts to hotfix-backed server logic.

For the hotfix loading model, dispatch publication, and development reload,
see [Hotfix Architecture](architecture.md). For BuildTag, packaging,
installation, production activation, and rollback, see
[Packaging and Deployment](../deployment.md).
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

Hotfix-owned Application HTTP declaration
  -> initial stable ASP.NET endpoint slot
  -> generation-pinned LakonaHttpCall
  -> current Server.Hotfix handler
```

## Decisions

- Shared contracts remain the source of truth for service, callback, and DTO
  shape.
- Hotfix HTTP classes remain the source of truth for service name, method,
  route, request/response boundary, and handler behavior.
- Application HTTP has no user-authored numeric method id. The stable host
  assigns process-local endpoint slots after validating the initial Hotfix
  manifest.
- `IHotfixServiceInvoker` is the required generated-dispatch gateway. It
  exposes numeric RPC method ids and host-assigned HTTP endpoint slots, not
  implementation method names. Do not add dynamic string dispatch overloads
  or default implementations that defer unsupported dispatch to runtime.
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

[RpcNotificationContract]
public interface IChatCallback
{
    [RpcNotification(RpcContractIds.ChatNotifications.MessageReceived)]
    void OnMessageReceived(ChatMessage msg);
}
```

The `NotificationContract` property is the single association authority
between the service and its callback; the callback interface carries only the
parameterless `[RpcNotificationContract]` marker. The stable server project
should not contain a service endpoint marker file or
hand-written service proxy files. There should be no user-authored equivalent
of:

```txt
Server/App/Services/GeneratedServiceEndpoints.cs
Server/App/Hosting/ServiceBindingConfigurator.cs
Server/App/Chat/ChatServiceProxy.cs
```

`Program.cs` is a thin infrastructure composition root. It registers only the
client-facing transport and serializer implementations selected during project
generation. The exact generated startup shape is owned by
[Generation Architecture](../tool/generation-architecture.md#server-renderers);
the framework-owned cluster channel is defined by
[Cluster RPC Composition](../cluster.md#cluster-rpc-composition).

Client endpoint transport and serializer names live in configuration;
generated `Program.cs` binds those names. It must not contain business services,
actor startup, cluster RPC selection, or generated RPC binding calls.
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

`Server.Hotfix` classes use `[LakonaHttpService]` and
`[LakonaHttpEndpoint]` directly:

```csharp
[LakonaHttpService("operations")]
public sealed class OperationsHttpService
{
    [LakonaHttpEndpoint("POST", "/operations/player/inspect")]
    public ValueTask<LakonaHttpResponse> InspectAsync(LakonaHttpCall call)
    {
        return new ValueTask<LakonaHttpResponse>(
            LakonaHttpResponse.Json(new { traceId = call.Request.TraceIdentifier }));
    }
}
```

Generated diagnostics validate the declaration at compile time. Runtime
scanning builds a manifest of service name, normalized HTTP method, route
pattern, and cached typed handler. A configured
`Lakona:Http:Listeners[]:Services` entry selects the service name on a physical
listener.

The initial Hotfix generation establishes the process-local route manifest and
the stable host assigns deterministic endpoint slots. Later candidates must
provide the same manifest; publication validation binds each slot to that
candidate's handler and rejects additions, removals, method changes, or route
changes. Handler method names are implementation details and may change.

`LakonaHttpCall` exposes the bounded stable request snapshot, cancellation
token, generation provider, Actor runtime, and game-server API. It deliberately
does not expose `HttpContext`. One admitted request holds one Hotfix runtime
lease through response materialization; a reload affects the next request, not
an already executing request. The snapshot is detached from `HttpContext` but
is not a deep hostile-code immutability boundary; handlers treat its
request-owned buffers and collections as read-only and must observe the
cooperative cancellation token.

Candidate validation checks every HTTP handler, not only the presence of a
service class. Each handler must be a public instance non-generic method with
one `LakonaHttpCall` parameter and the exact
`ValueTask<LakonaHttpResponse>` return type. Duplicate service names, duplicate
routes, missing configured services, reserved management routes, manifest
drift, and mismatched handlers prevent publication.

### Hotfix Service Dependencies

Constructor injection applies to container-owned services and components.
Business exceptions deriving directly or indirectly from `System.Exception`
are constructed with `new` using runtime data; they need neither
`[HotfixComponent]` nor DI registration and may remain in Hotfix.
Adding that marker is an error (`LAKONA20060`); remove it instead of registering
error codes or inner exceptions as services. Unmarked exception types do not
enter automatic component registration or component dependency precheck.

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
the shared `IChatService` contract. All generated call types expose the request,
connection id, current Game Session and item snapshot, hotfix services, Actors,
and game-server APIs. They do not expose a connection-scoped callback. Game
business notifications publish through constructor-injected
`IClientNotifications`, normally behind a domain notifier, and explicitly select
the target `GameSessionKey`. This keeps reliable push, best-effort delivery,
cluster routing, admission, and reconnect behavior behind one session-oriented
interface regardless of which Hotfix method originated the notification.

The framework-level `HotfixServiceCall<TRequest>` struct remains a dispatch
support type. Generated projects should author handlers with the service-scoped
generated call type instead. Before a Game Session exists, business RPC uses its
response DTO or establishes a session; Hotfix code does not gain a second
connection-targeted notification path for that phase.

The hotfix startup method marked `[HotfixConfigureServices]` registers
dependencies used by hotfix logic. It does not register `[HotfixService]`
classes themselves. The dispatch layer owns service implementation lifetime and
creates one instance per published hotfix generation.

A Hotfix assembly has at most one `[HotfixStartup]` composition root. The root
may call any number of explicitly ordered service-registration extension
methods, and normal Microsoft DI ordering and last-registration semantics apply
within that one `[HotfixConfigureServices]` call. Multiple startup roots are
not merged or assigned implicit priorities: the candidate is rejected before
any startup method runs, with conflicts reported in stable type-name order.

Constructor parameters resolve from the current hotfix generation provider
first and the stable root provider second. Generation-local dependencies should
be registered through the `[HotfixConfigureServices]` startup method; stable
framework dependencies should come from the root provider and must not capture
`Server.Hotfix` types.

This resolution order is a permanent part of the Hotfix authoring model:

- generation-local registrations may deliberately shadow stable registrations;
- stable application-module services are automatically available through their
  stable business interfaces;
- constructor signatures remain the declaration of a Hotfix class's
  dependencies;
- missing dependencies fail candidate activation before publication.

`LAKONA20057` reports constructor dependency cycles between automatically
registered `[HotfixComponent]` classes at compile time, including
`IEnumerable<Component>` dependencies. It uses the selected activation
constructor and reports the cycle path at the dependency parameter. Break the
cycle by removing the back-reference or extracting a shared dependency.
If the assembly declares custom service registration, `LAKONA20058` reports
the same graph as a warning: a factory or replacement registration may change
the actual graph. Interface, keyed, and factory dependencies require runtime
registration information and are not inferred by this analyzer.

During candidate activation, re-entering the same guarded registration fails
with an actionable dependency-cycle error instead of recursively activating
through the stable-provider fallback. Tracking uses registration identity, so
legitimate multiple registrations of one service type remain supported. A failed
candidate leaves the current generation published and permits a later corrected
reload.

Before activating dispatch modules, validation also inspects the effective
registrations of every `[HotfixComponent]`, including components only resolved
later from a request. For implementation types with one public constructor it
walks constructor dependencies without creating instances. Known missing
registrations, cycles, invalid implementation types, and a singleton's
generation-local scoped dependencies reject the candidate with a dependency
chain and repair advice. Exact registrations take precedence over open generic
registrations; the last registration wins for a single service. Collections
check every applicable local registration, allow empty results, and exclude
open registrations whose generic constraints cannot be satisfied. Optional
parameters may use their default when the service is absent.
An explicit collection registration replaces the synthesized local collection.

Open generic implementation registrations retain native DI activation, so their
constructor parameters must be available in the generation container (or have
optional defaults). Stable-provider registrations cannot satisfy these direct
dependencies. Register the dependency locally or use an explicit closed generic
registration to enable fallback activation. A local non-generic dependency has
its own fallback activation and may still depend on stable services; the
precheck follows this distinction for each constructor.

A stable provider's `IServiceProviderIsService` metadata establishes availability
without resolving stable services. Its internal dependency graph and lifetimes
are not inspected. Explicit instances replace constructor requirements. Factory
registrations, keyed parameters, multiple public constructors, recursively
expanding generic registrations, and stable providers without service metadata
produce coverage warnings instead of speculative missing-service errors. These
paths are not executed by the metadata precheck. Successful validation/reload
with coverage warnings reports `SucceededWithWarnings`; admin responses and
status include `lastOperationWarnings`. Exercise the indicated resolution in an
activation test before deployment. The precheck does not analyze service-locator
calls inside user methods or impose a timeout on arbitrary user code.

Do not add a second export registry, stable-service bridge, or allow-list that
duplicates root-provider registration. It would not remove the stable and
generation-local object graphs because they have different owners and
lifetimes, and a generic service-locator bridge would make constructor
dependencies less explicit. An explicit capability boundary becomes
appropriate only if Hotfix is later allowed to host untrusted third-party code
or receives a concrete tenant-isolation requirement.

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

Generated RPC dispatch uses stable numeric method ids. Application HTTP uses
host-assigned endpoint slots. Both paths use cached typed delegates. Warm
service, HTTP, and Actor calls must not construct method-name keys, type arrays,
argument arrays, or invoke user methods through reflection. Static service
methods remain supported for stateless RPC helpers, but HTTP handlers are
instance methods on their service class.

## Session Lifecycle Boundary

Lifecycle modules use `[HotfixLifecycle]` and directly implement their stable
interfaces, for example `class ChatSessionLifecycle : IGameSessionLifecycle`.
The runtime discovers implemented lifecycle interfaces and calls them directly,
including explicit and inherited implementations. Session handlers are selected
with `StartSessionAsync<TLifecycle>` and may have multiple implementations of
`IGameSessionLifecycle`; other lifecycle contracts retain unique interface binding.
Session selections retain stable names only, and publication rejects removal of
a handler referenced by a live session or pending expiration callback.
Lifecycle contracts accept
`HotfixLifecycleCall<TRequest>` themselves. This wrapper contains only `Request`;
dependencies are constructor-injected, and it does not implement `IHotfixCallContext`.
No method attributes or numeric IDs
are required. The caller holds a runtime lease until the callback completes,
preserving generation services and timer scope. One generation owns one instance per module,
even when that module implements several lifecycle contracts. Ordinary RPC
`[HotfixService(typeof(...))]` binding retains its request-to-call adaptation.

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

Generated docs use the three routine editing areas defined by
[Default Experience](../tool/default-experience.md#user-facing-project-shape).
For binding specifically, shared contracts belong in `Shared/Contracts/**`,
stable generated binding remains in `Server/App/**`, and user-authored RPC,
Application HTTP, lifecycle, and Actor behavior implementations belong in
`Server/Hotfix/**`.

Generated projects must not teach users to put presence cleanup, matchmaking
cleanup, room leave policy, or session business policy in `Server.App`.
When framework runtime code observes a lifecycle event, the framework-owned
bridge acquires the current runtime lease and forwards the event through a
direct lifecycle interface call. Do not name user lifecycle
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

Allow those names only in negative tests that verify they remain forbidden or
in release notes.
