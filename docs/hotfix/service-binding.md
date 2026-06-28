# Generated Hotfix Service Binding

This document describes how generated Lakona projects bind shared RPC service
contracts to hotfix-backed server logic.

For the hotfix loading model, dispatch publication, `BuildTag`, development
reload, and production activation, see [Hotfix Architecture](architecture.md).
For game session identity, callback binding, disconnect, resume, and
termination semantics, see [Session Lifecycle](../session.md).

## Purpose

Generated Lakona projects should not require users to hand-write stable RPC
service proxies, binder configuration, service endpoint marker files, or raw
`RpcSession` lifecycle subscriptions when they add a new service.

The default model is:

```txt
Shared RPC contract
  -> generated stable Server.App binding
  -> current Server.Hotfix service method
  -> framework-owned session lifecycle APIs when needed
```

## Decisions

- Shared contracts remain the source of truth for service, callback, and DTO
  shape.
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

`Program.cs` is strict zero-template:

```csharp
using Lakona.Game.Server.Hosting;

return await LakonaGameServer.RunAsync(args);
```

Transport and serializer selection live in `Lakona:Endpoints[]`; generated
`Program.cs` must not hand-write transport, serializer, or acceptor wiring.
Generated hotfix-backed RPC services are selected by endpoint-local
`RpcServices`. The generator emits `LakonaRpcServiceBinder` adapters and
`IHotfixRequiredServiceContracts` providers; `LakonaGameServer.RunAsync`
discovers both automatically from the application assembly.

Stable service proxy generation is app-side only. The same hotfix generator
package may run in both `Server.App` and `Server.Hotfix`, but
`Server.Hotfix` must not emit another `*ServiceProxy`, endpoint binder, or
`IHotfixRequiredServiceContracts` provider for shared RPC contracts. Hotfix
projects compile the replaceable `[HotfixService]` and `[HotfixLifecycle]`
implementations and any behavior-owned actor wrapper extensions; stable RPC
binding remains in `Server.App`.

There is no user-authored `.UseGeneratedHotfixServices()` step in generated
projects. Generated binders and required-contract providers are framework
discovery artifacts, not fluent host calls that users copy into `Program.cs`.

When a user adds a new shared `[RpcService]` interface and implements a matching
hotfix `[HotfixService]`, no stable proxy file, binding configurator, endpoint
marker, or endpoint name should be written by hand.

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

    public ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)
    {
        // Use _presence for dependencies and call for request-specific data.
        return default;
    }
}
```

`HotfixFeatureContext.Services` registers dependencies used by hotfix logic.
It does not register `[HotfixService]` classes themselves. The dispatch layer
owns service implementation lifetime and creates one instance per non-static
service call.

Constructor parameters resolve from the current hotfix generation provider
first and the stable root provider second. Generation-local dependencies should
be registered through `HotfixFeatureContext.Services`; stable framework
dependencies should come from the root provider and must not capture
`Server.Hotfix` types.

Non-static `[HotfixService]` and `[HotfixLifecycle]` implementation classes
must have one public constructor, or one public constructor marked with
`[ActivatorUtilitiesConstructor]`. Missing dependencies, open generic
implementations, multiple unmarked public constructors, and activation failures
fail hotfix validation or reload before the candidate generation is published.

The dispatch layer disposes the per-call service or lifecycle instance after
the returned `ValueTask` completes, including failure paths. Constructors
should capture and validate dependencies only; they must not start timers,
threads, static event subscriptions, long-lived connections, or request work.

For high-frequency realtime methods, a service method may stay static to avoid
allocating one service implementation instance per request. Keep that exception
local to the hot method and resolve only the required dependencies from
`call.Services`.

## Session Lifecycle Boundary

Generated binding code may call session-oriented game server APIs when a
service needs to start a game session, bind callbacks, look up callbacks, or
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
