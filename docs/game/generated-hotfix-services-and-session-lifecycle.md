# Generated Hotfix Services And Session Lifecycle

Status: initial implementation complete
Date: 2026-06-12
Audience: maintainers and implementation agents

Implementation note: the first implementation keeps
`HotfixServiceCall<TRequest>` and `HotfixServiceCall<TRequest, TCallback>` in
`Lakona.Game.Server` under the `Lakona.Game.Server.Hotfix` namespace, rather
than in `Lakona.Game.Server.Hotfix.Abstractions`. The call context exposes
`IActorRuntime`, `ILakonaGameServer`, and `GameEndpointName`; putting it in the
abstractions package would make that package depend back on `Lakona.Game.Server`
and create a circular package boundary.

## Purpose

Generated Lakona.Game projects should not require users to hand-write stable
RPC service proxies, binder configuration, or raw `RpcSession` disconnect
tracking when they add a new service.

The long-term model is:

```txt
Shared RPC contract
  -> generated stable Server.App proxy and binding
  -> current Server.Hotfix service method
  -> framework-owned connection and game-session lifecycle
  -> user-owned business presence hooks
```

This document defines the target architecture for two related changes:

- Generate stable hotfix service proxies and RPC bindings for user services.
- Move RPC connection and game-session lifecycle management into the framework,
  while exposing explicit hooks for game-specific policy.

The project is still early, so implementation does not need compatibility
branches for the current sample shape.

## Current Problem

The Godot chat sample and new `Lakona.Tool` projects currently include files
such as:

```txt
Server/App/Chat/LoginServiceProxy.cs
Server/App/Chat/ChatServiceProxy.cs
Server/App/Hosting/ServiceBindingConfigurator.cs
Server/App/Chat/ChatConnectionLifecycle.cs
```

The service proxy and service binding files are template code. Their shape is
mechanically determined by the shared RPC service contract, notification
contract, hotfix dispatch API, and generated RPC binder names. Requiring users
to duplicate that shape for every new service makes the framework feel like a
starter template instead of a runtime.

`ChatConnectionLifecycle` exposes a deeper lifecycle issue. It directly
subscribes to `RpcSession.Disconnected` and immediately runs chat-room leave
logic. That is fine for a small demo, but it conflates three distinct events:

- a transport connection was lost
- a game endpoint was disconnected and may still be resumable
- a user should leave a room or be removed from business state

The framework already has `ILakonaGameServer`, `IGameSessionDirectory`,
endpoint bindings, resume, termination, reliable push, and disconnected-endpoint
cleanup. What is missing is a framework-owned bridge from low-level RPC
connection lifetime to those session primitives.

## Design Goals

- Adding a new hotfix-backed RPC service must not require hand-written stable
  proxy or binder code.
- Generated code remains source-generator output. Do not commit generated RPC
  glue or generated hotfix service glue into user projects.
- `Server.App` remains the stable boundary and must not reference
  `Server.Hotfix` as a normal compile-time dependency.
- RPC packages remain transport and dispatch infrastructure. They must not
  depend on `Lakona.Game`.
- The game framework owns connection tracking, endpoint binding lifecycle,
  disconnect marking, disconnected endpoint expiration, and termination
  publication.
- User code owns authentication, owner keys, matchmaking, room membership,
  gameplay state, and disconnect policy.
- A raw disconnect must not automatically mean business leave. Reconnect and
  retention policies must stay explicit.
- The design must support one endpoint first and multi-endpoint control plus
  realtime flows without changing vocabulary later.

## Non-Goals

- Do not make Lakona.Game own account schemas, room rules, matchmaking rules,
  inventory, battle state, or product DTOs.
- Do not make hotfix services long-lived session owners. Hotfix code remains
  replaceable request and behavior logic.
- Do not load `Server.Hotfix` into the default `AssemblyLoadContext`.
- Do not solve this by making `Lakona.Rpc.Analyzers` depend on
  `Lakona.Game.Server.Hotfix`.
- Do not expose `RpcSession` as the normal user lifecycle extension point.

## Vocabulary

| Term | Meaning |
| --- | --- |
| RPC connection | One accepted transport connection owned by `Lakona.Rpc.Server`. |
| Connection id | Stable id for the RPC connection, currently aligned with `RpcSession.ContextId`. |
| Game endpoint | A logical game endpoint such as `control`, `realtime`, or `default`. |
| Endpoint binding | A `GameSessionKey + GameEndpointName + callback contract -> connection id + callback` record. |
| Game session | Framework-owned identity for a player's current resumable session generation. |
| Business presence | Product-specific state such as room membership or lobby presence. |

## Target User Experience

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

The stable server project opts services into generated hotfix dispatch with a
small marker file:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Shared.Contracts.Chat;

namespace Server.App.Services;

[HotfixRpcService(typeof(ILoginService), EndpointName = "control")]
internal static partial class LoginServiceEndpoint;

[HotfixRpcService(typeof(IChatService), EndpointName = "control")]
internal static partial class ChatServiceEndpoint;

[HotfixRpcService(
    typeof(IRealtimeService),
    BindingSetName = "realtime",
    EndpointName = "realtime")]
internal static partial class RealtimeServiceEndpoint;
```

`EndpointName` is a logical game endpoint, not each service contract on that
channel. It describes the role of the RPC session, such as `"control"` or
`"realtime"`. Generated projects may use `"control"` to align with session
termination and reliable-push examples. The generator should default to
`"default"` when omitted.

One RPC session belongs to exactly one `GameEndpointName`. In a multi-endpoint
game, the control WebSocket listener accepts control RPC sessions, and the KCP
realtime listener accepts realtime RPC sessions. The same player or
`GameSessionKey` may be bound to both, but those are two RPC sessions and two
endpoint bindings. Business code decides when a player, character, room, or
matchmaking flow should bind those endpoints; the framework only owns the
generic session/endpoint/connection bookkeeping.

One RPC session can carry multiple callback contracts for the same
`GameEndpointName`. For example, a single WebSocket control connection may hold
`ILoginCallback`, `IChatCallback`, and `ILakonaGameSessionCallback` callback
proxies. The session directory should therefore key endpoint bindings by
`GameSessionKey + GameEndpointName + callback contract type`.

The RPC registry is still keyed by RPC service id and method id. Each generated
binding set has exactly one `GameEndpointName`. Within one binding set, a
shared service contract can be registered only once. If the same contract must
be exposed through multiple listener registries, the generator should support
explicit binding-set names and treat uniqueness as `(binding set, service
contract)`. The default binding set is the one used by
`UseGeneratedHotfixServices()`.

The generator must not silently split one binding set into multiple endpoint
registries. If markers in the same `BindingSetName` declare different
`EndpointName` values, generation should fail with a diagnostic that tells the
user to give each endpoint an explicit binding-set name. For example, control
services can use the default binding set while realtime services use
`BindingSetName = "realtime"` and are bound by the realtime listener.

`Program.cs` binds generated services through one framework-facing extension:

```csharp
return await LakonaGameServer.RunAsync(args, server => server
    .UseTransport("kcp")
    .UseSerializer(() => new MemoryPackRpcSerializer())
    .UseAcceptor(opts => Task.FromResult<IRpcConnectionAcceptor>(
        new KcpConnectionAcceptor(opts.Port, opts.Host)))
    .UseGeneratedHotfixServices());
```

`UseGeneratedHotfixServices()` is generated into `Server.App`. It should call a
framework API that records the endpoint name, creates per-RPC-session callback
proxies, constructs generated stable service proxies, and binds them through
the generated RPC service binders.

Callback proxies are tied to `RpcSession`, not `GameSessionKey`. Login and
resume happen before the framework can know the game session. The generated
stable proxy receives the callback proxy for the current RPC session and passes
it through the hotfix call context. Hotfix login or bind logic later stores that
same callback proxy in the framework endpoint binding when it calls
`StartSessionAsync`, `ResumeSessionAsync`, or `BindEndpointAsync`. Storing a
new callback contract for the same endpoint must not overwrite another callback
contract already bound to that endpoint. Rebinding the same callback contract
for the same `GameSessionKey + GameEndpointName` replaces the previous
connection id, callback instance, bound timestamp, and disconnected state for
that callback contract only.

Hotfix service code accepts a framework call context rather than a project-local
call record:

```csharp
[HotfixService(typeof(ILoginService))]
internal sealed class LoginService
{
    public static async ValueTask<LoginReply> LoginAsync(
        HotfixServiceCall<LoginRequest, ILoginCallback> call)
    {
        var ownerKey = call.Request.PlayerName.Trim();
        var session = await call.GameServer.StartSessionAsync(
            ownerKey,
            call.EndpointName,
            call.ConnectionId,
            call.Callback);

        return new LoginReply();
    }
}

[HotfixService(typeof(IChatService))]
internal sealed class ChatService
{
    private static readonly ActorId RoomId = ActorId.From("chat:global");

    public static async ValueTask BindAsync(
        HotfixServiceCall<ChatBindRequest, IChatCallback> call)
    {
        await call.Actors.AskAsync<ChatRoomActor, bool>(
            RoomId,
            (room, ct) =>
            {
                room.BindChatCallback(call.ConnectionId, call.Callback);
                return new ValueTask<bool>(true);
            });
    }
}
```

The call context should expose only stable runtime dependencies:

```csharp
public class HotfixServiceCall<TRequest>
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        GameEndpointName endpointName,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId;
        EndpointName = endpointName;
        Services = services;
        Actors = actors;
        GameServer = gameServer;
    }

    public TRequest Request { get; }
    public string ConnectionId { get; }
    public GameEndpointName EndpointName { get; }
    public IServiceProvider Services { get; }
    public IActorRuntime Actors { get; }
    public ILakonaGameServer GameServer { get; }
}

public sealed class HotfixServiceCall<TRequest, TCallback> :
    HotfixServiceCall<TRequest>
    where TCallback : class
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        GameEndpointName endpointName,
        TCallback callback,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : base(request, connectionId, endpointName, services, actors, gameServer)
    {
        Callback = callback;
    }

    public TCallback Callback { get; }
}
```

This removes project-local `LoginServiceCall` and `ChatServiceCall` records from
the default shape. If a game needs a richer domain-specific command, hotfix
service code can construct it inside the hotfix method.

Return mapping stays one-to-one with the shared RPC contract. For a contract
method returning `ValueTask<TResult>`, the hotfix method returns
`ValueTask<TResult>`, and the generated stable proxy calls
`IHotfixServiceInvoker.InvokeAsync<TContract, TCall, TResult>`. For a contract
method returning `ValueTask`, the hotfix method returns `ValueTask`, and the
generated proxy calls `IHotfixServiceInvoker.InvokeAsync<TContract, TCall>`.
The generated RPC binder serializes the returned `TResult` exactly as it does
for ordinary stable service implementations.

The hotfix service dispatch key should use the stable RPC method id from
`[RpcMethod]`, not the C# method name. The shared RPC contract is the source of
truth, and method ids already define wire compatibility. Using method ids avoids
ambiguity when a C# method is renamed and avoids relying on overload semantics.
Human-readable diagnostics may still include the C# method name.

Hotfix scanning should map each `[HotfixService(typeof(TContract))]` method to
exactly one method on `TContract`, read that contract method's `[RpcMethod]`
id, and publish the hotfix service binding under `(contract type, method id)`.
If no contract method matches, more than one contract method matches, or the
contract method lacks `[RpcMethod]`, scanning must reject the hotfix method with
a diagnostic. The first implementation can require matching C# method names
between the hotfix service and the contract while still using the method id as
the dispatch key.

Generated proxy shape for a returning method:

```csharp
public ValueTask<LoginReply> LoginAsync(LoginRequest req)
{
    return _hotfix.InvokeAsync<
        ILoginService,
        HotfixServiceCall<LoginRequest, ILoginCallback>,
        LoginReply>(
        RpcContractIds.LoginServiceMethods.LoginAsync,
        new HotfixServiceCall<LoginRequest, ILoginCallback>(
            req,
            _connectionId,
            _endpointName,
            _callback,
            _services,
            _actors,
            _gameServer));
}
```

## Source Generation Model

`Lakona.Game.Server.Hotfix.Generators` owns hotfix service proxy generation.
The generator should discover `[HotfixRpcService]` declarations in `Server.App`
and inspect the referenced shared contract type.

For each marker, generate:

- an internal stable proxy implementing the shared RPC service interface
- one method implementation per `[RpcMethod]`
- construction of `HotfixServiceCall<TRequest>` or
  `HotfixServiceCall<TRequest, TCallback>`
- service binding that uses the generated RPC binder for the contract
- callback proxy construction when the shared contract declares
  `NotificationContract`
- a generated extension such as `UseGeneratedHotfixServices`

The generator must reject unsupported service shapes with diagnostics:

- the marker type is not `partial`
- the contract type is not an interface marked `[RpcService]`
- an RPC method does not have exactly one request DTO parameter
- an RPC method does not return `ValueTask` or `ValueTask<TResult>`
- callback metadata is inconsistent with the generated RPC service model
- duplicate hotfix service markers target the same service contract in one
  generated binding set
- markers in one generated binding set declare more than one endpoint name

The generator must not parse generated source text from `Lakona.Rpc.Analyzers`.
Implementation should either share a small RPC service model/naming helper or
define a stable generated-symbol contract that both generators can rely on. The
hotfix generator may mirror RPC binder naming only if tests lock the two
generators together.

Generated proxies must remain in `Server.App` and call
`IHotfixServiceInvoker`. The `IHotfixServiceInvoker` continues to resolve the
current hotfix dispatch table, so existing connections use new hotfix service
logic on the next call after reload.

## RPC And Game Lifecycle Bridge

`Lakona.Rpc.Server` should expose neutral lifecycle hooks without referencing
`Lakona.Game`:

```csharp
public sealed record RpcSessionLifecycleContext(
    string ConnectionId,
    string DisplayName);

public interface IRpcSessionLifecycleObserver
{
    ValueTask OnSessionStartedAsync(
        RpcSessionLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionDisconnectedAsync(
        RpcSessionLifecycleContext context,
        Exception? error,
        CancellationToken cancellationToken = default);
}
```

The exact type names can change, but the boundary matters: RPC reports session
lifetime; game hosting interprets it.

`Lakona.Game.Server` should register an observer that turns RPC lifetime into
game connection lifetime:

```txt
RPC session started
  -> Game connection opened
  -> optional user lifecycle hooks

RPC session disconnected
  -> mark every endpoint aggregate bound to that connection disconnected
  -> publish endpoint-disconnected lifecycle hooks
  -> disconnected endpoint cleanup later expires stale endpoint aggregates
  -> publish endpoint-expired lifecycle hooks
```

Endpoint disconnection and endpoint expiration are not session termination.
They affect callback availability and business presence only. A game session is
terminated only by an explicit terminal framework operation, such as
`ILakonaGameServer.TerminateSessionAsync`, or by a framework-owned replacement
operation that intentionally invalidates the previous session generation for an
owner. When a session is terminated, the framework publishes
`OnSessionTerminatedAsync` separately from endpoint-disconnected and
endpoint-expired hooks.

This requires the session directory or a companion connection tracker to support
connection-id lookups. Prefer putting the lookup in the session directory
because it already owns endpoint bindings and connection ids.

Suggested new directory operations:

```csharp
ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>>
    MarkConnectionDisconnectedAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>>
    ExpireDisconnectedEndpointsAsync(
        DateTimeOffset disconnectedBefore,
        CancellationToken cancellationToken = default);
```

The current `ExpireDisconnectedEndpointsAsync` can be changed because backward
compatibility is not required.

Endpoint lifecycle publication is aggregate-level, not callback-contract-level.
For lifecycle purposes, an endpoint is identified by
`GameSessionKey + GameEndpointName + connection id`. `OnEndpointBoundAsync`,
`OnEndpointDisconnectedAsync`, and `OnEndpointExpiredAsync` should each fire at
most once for that aggregate state transition, even if the endpoint contains
multiple callback contracts. This prevents presence cleanup from running once
per callback contract.

Callback binding changes are storage details. Binding `ILoginCallback` and then
`IChatCallback` on the same control RPC session should not publish two endpoint
bound events. Rebinding the same callback contract replaces only that callback
contract binding. If the endpoint aggregate was disconnected and a callback
binding makes it active again, the framework may publish one endpoint-bound
event for the aggregate resume.

## User Lifecycle Hooks

User hooks should receive game-level context, not `RpcSession`:

```csharp
public interface IGameSessionLifecycleHandler
{
    ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnEndpointBoundAsync(
        GameEndpointBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnEndpointDisconnectedAsync(
        GameEndpointBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnEndpointExpiredAsync(
        GameEndpointBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default);
}
```

Separate smaller interfaces are also acceptable if implementation shows that
single-method hooks are easier to compose. The important rule is that hooks are
ordered framework events, not raw transport callbacks.

Business presence policy belongs in these hooks. For example, a chat project
should remove a member from a room on endpoint expiration or session
termination, not automatically on every transient disconnect. If a game wants
disconnect to end the whole game session, its endpoint-expired handler can call
`TerminateSessionAsync`. If a game wants immediate leave semantics without
terminating the session, it can set disconnected endpoint retention to zero or
handle `OnEndpointDisconnectedAsync` directly.

Hook failures must be contained:

- Framework state transitions happen before hook invocation.
- A hook exception is logged and surfaced through diagnostics.
- One failing hook must not stop later hooks from running.
- Hooks should receive cancellation tokens but must not block shutdown
  indefinitely.

## Endpoint Binding Semantics

Login and resume remain business decisions.

The framework should not infer owner keys, account ids, or session ids from
transport connections. Hotfix login code should still call
`ILakonaGameServer.StartSessionAsync` or `ResumeSessionAsync` after it validates
the user's credentials or resume token.

Once a service binds an endpoint:

```csharp
await call.GameServer.BindEndpointAsync(
    session,
    call.EndpointName,
    call.ConnectionId,
    call.Callback);
```

the framework owns the connection-id association for that endpoint and callback
contract. When the connection closes, the lifecycle bridge marks every endpoint
binding associated with that connection disconnected. Cleanup later removes
expired disconnected endpoint bindings according to `SessionCleanupOptions`.

Multi-endpoint games should bind each endpoint independently. Control and
realtime channels can disconnect and resume independently while sharing the
same `GameSessionKey`. The common low-latency model is separate control and
realtime RPC sessions. Callback contracts on the same logical channel should be
multiple bindings under one endpoint name, not artificial endpoint names per
service.

## Generated Project Shape

New `Lakona.Tool` output should stop rendering stable proxy and binder files.

Remove from generated default projects:

```txt
Server/App/Chat/LoginServiceProxy.cs
Server/App/Chat/ChatServiceProxy.cs
Server/App/Hosting/ServiceBindingConfigurator.cs
Server/App/Chat/ChatConnectionLifecycle.cs
```

Add:

```txt
Server/App/Services/GeneratedServiceEndpoints.cs
Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs
```

`GeneratedServiceEndpoints.cs` should contain only partial marker declarations.
It should not contain copied generated glue or business lifecycle registration.
Sample lifecycle policy should live in focused files such as
`Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs`.

Lifecycle hook registration should happen in normal startup composition, either
in `Program.cs` through `AddServices` or inside a project Feature
`ConfigureServices` method:

```csharp
.AddServices(services =>
{
    services.AddSingleton<IGameSessionLifecycleHandler, ChatPresenceLifecycleHandler>();
})
```

Generated docs should teach three edit zones:

- `Shared/Contracts/**`: define service, callback, and DTO contracts.
- `Server/App/Services/GeneratedServiceEndpoints.cs`: opt shared services into
  generated stable hotfix dispatch.
- `Server/App/Lifecycle/**`: implement stable business lifecycle hooks such as
  presence cleanup.
- `Server/Hotfix/**`: implement hot-reloadable service and actor behavior logic.

## Interaction With Hotfix BuildTag

Generated service proxy shape, call context shape, endpoint marker attributes,
and lifecycle hook contracts are stable boundary visible to hotfix code. Changes
to those shapes require a `BuildTag` update in generated projects.

Pure hotfix method body changes do not require a `BuildTag` update.

## Testing And Validation Requirements

Implementation must add focused tests at these boundaries:

- `Lakona.Rpc.Server.Tests`: RPC session lifecycle observers receive started
  and disconnected events exactly once per accepted connection.
- `Lakona.Game.Server.Tests`: a disconnected RPC connection marks all endpoint
  bindings for that connection disconnected.
- `Lakona.Game.Server.Tests`: expired disconnected endpoints are returned to
  lifecycle publishing and no longer returned as active callbacks.
- `Lakona.Game.Server.Tests`: lifecycle hook exceptions are logged or captured
  without preventing state transitions.
- `Lakona.Game.Server.Hotfix.Generators.Tests`: service marker generation emits
  stable proxy, call context construction, and generated binding extension.
- `Lakona.Game.Server.Hotfix.Generators.Tests`: unsupported contracts produce
  diagnostics instead of invalid generated code.
- `Lakona.Tool.Tests`: generated projects no longer contain hand-written
  `*ServiceProxy.cs`, `ServiceBindingConfigurator.cs`, or raw
  `RpcSession.Disconnected` tracking.
- Sample or tool E2E: a generated Godot or Unity project builds, starts after
  hotfix build output exists, logs in, sends a chat message, disconnects, and
  exercises lifecycle cleanup.

Source-scan tests should explicitly reject these patterns in generated projects:

```txt
class LoginServiceProxy
class ChatServiceProxy
ServiceBindingConfigurator
RpcSession.Disconnected +=
```

They should allow those names only inside source-generator tests when asserting
generated output shape.

## Implementation Handoff

Recommended implementation order:

1. Add neutral RPC session lifecycle observer support in `Lakona.Rpc.Server`.
2. Add game connection/session lifecycle bridge in `Lakona.Game.Server`.
3. Add `HotfixServiceCall<TRequest>` and
   `HotfixServiceCall<TRequest, TCallback>` to the hotfix abstractions package,
   and update hotfix service dispatch to key by RPC method id instead of C#
   method name.
4. Add `[HotfixRpcService]` and source generation in
   `Lakona.Game.Server.Hotfix.Generators`.
5. Update `LakonaGameServerBuilder` or generated extension points so generated
   hotfix services can be bound with one call from `Program.cs`.
6. Update `Lakona.Tool` renderers to emit marker declarations instead of
   stable proxy and binder source files.
7. Update `samples/Game.Godot.Chat` to use generated service bindings and
   framework lifecycle hooks.
8. Update generated project docs and relevant runtime docs.
9. Run targeted generator, runtime, tool, and sample validation.
10. Bump affected package versions under `src/**` when code changes are ready
    for release, following `CONTRIBUTING.md`.

Each phase should leave the solution buildable. Prefer adding tests before
changing runtime behavior because this design touches shared hosting,
generation, and hotfix boundaries.

## Post-Implementation Review Follow-Ups

The first implementation is close to the target shape, but review found several
implementation gaps that should be fixed before treating this design as done.
These are not open design choices; they are corrections needed to satisfy the
contracts described above.

1. Publish every declared game lifecycle hook.

   `IGameSessionLifecycleHandler` declares `OnEndpointBoundAsync` and
   `OnSessionTerminatedAsync`, but the current runtime paths must publish them
   explicitly. A successful endpoint aggregate bind should publish
   `OnEndpointBoundAsync` at most once for
   `GameSessionKey + GameEndpointName + connection id`, even when multiple
   callback contracts are added to that same aggregate. `TerminateSessionAsync`
   should publish `OnSessionTerminatedAsync` after terminal session state is
   recorded, regardless of whether a client callback is currently available.
   Handler failures should be logged or captured without rolling back the
   already-committed session state transition.

   Add tests for initial bind, resume bind, same-callback rebinding,
   multi-callback binding on one endpoint, termination with a live callback, and
   termination with no live callback.

2. Ensure default generated hosts actually run endpoint expiration cleanup.

   The default generated chat lifecycle handler puts business cleanup in
   `OnEndpointExpiredAsync`. That hook will never run unless
   `GameSessionCleanupHostedService` is registered. The default hosted game
   server path or the generated `Program.cs` template must register session
   cleanup when generated lifecycle handlers depend on endpoint expiration.
   Do not leave generated projects with cleanup handlers that are registered but
   unreachable.

   Add a tool or sample test that builds a generated project service collection
   and verifies both the lifecycle handler and `GameSessionCleanupHostedService`
   are registered. Keep the retention policy explicit through
   `SessionCleanupOptions`.

3. Make generated hotfix service bindings namespace-safe.

   `GeneratedHotfixServicesExtensions` may be generated in the namespace of the
   first marker type, while each generated proxy is emitted next to its own
   marker type. Binding code must therefore instantiate proxy types with fully
   qualified names, or the generator must place all proxies and the extension in
   one known generated namespace. Otherwise, adding a service marker in a
   different namespace can produce uncompilable generated source.

   Add generator tests with service markers in different namespaces, including
   markers in different binding sets, and assert the generated source compiles.

4. Report diagnostics for unsupported hotfix service contract shapes.

   Unsupported service shapes must not be silently skipped and must not produce
   invalid generated source. The generator should emit diagnostics when the
   contract is not an interface marked `[RpcService]`, an RPC method lacks
   `[RpcMethod]`, a method has anything other than one request DTO parameter, a
   method returns anything other than `ValueTask` or `ValueTask<TResult>`, or
   callback metadata cannot be mapped to a generated callback proxy.

   Add generator tests that assert diagnostics for each unsupported shape and
   assert no partial proxy implementation is emitted for the invalid contract.

As of commit `94cb5ce703482f208eca8a0d8622bb411cf8feff`, the four items above
have implementation and focused test coverage. Keep them documented as the
acceptance criteria for this feature family.

## Second-Pass Review Follow-Ups

Review of commit `94cb5ce703482f208eca8a0d8622bb411cf8feff` found these
remaining issues before the work should be considered complete.

1. Update all `IGameSessionDirectory.BindEndpointAsync` call sites after the
   return type change.

   `IGameSessionDirectory.BindEndpointAsync` now returns
   `ValueTask<GameSessionEndpointBindResult>`. Direct `await` call sites can
   discard the result, but forwarding methods returning plain `ValueTask` must
   be updated. The current Unity Agar gateway sample still has
   `SessionDirectory.BindControlAsync` returning `ValueTask` while returning the
   directory call directly. That shape is incompatible with the new interface.

   Fix by either making the forwarding method `async` and awaiting the directory
   call, or by changing the forwarding method to return the new result type if
   callers need the bind transition. Add a focused compile or unit test for this
   gateway service path.

2. Add sample-server validation for `Game.Unity.Agar`.

   `dotnet build samples/Game.Unity.Agar/Server/Gateway/Gateway.csproj
   --no-restore` currently fails before reaching the bind-return mismatch
   because `Gateway.Generated` is not available to the gateway project. This
   sample is not covered by the root `Lakona.slnx` build, so regressions in the
   larger multiplayer sample can survive the normal repository validation loop.

   Restore the gateway generated RPC binding setup or update the sample to the
   current generated-binding pattern, then add this project or an equivalent
   sample E2E check to the validation list used before release.

3. Bump package versions for shippable package changes.

   This commit changes shippable source under `src/Lakona.Game.Server`,
   `src/Lakona.Game.Server.Hotfix.Generators`, and `src/Lakona.Tool`, but the
   package versions remain unchanged from the previous commit. Before release,
   bump the affected `.csproj` versions according to `CONTRIBUTING.md`, and
   update any tool template version constants, sample package references, and
   changelog entries that are tied to those package versions.

## Third-Pass Review Follow-Up

Review of the follow-up changes after
`94cb5ce703482f208eca8a0d8622bb411cf8feff` found one remaining validation
issue.

1. Make Unity Agar gateway build validation independent of pre-existing restore
   artifacts.

   `UnityAgarGatewayBuildTests` shells out to `dotnet build
   samples/Game.Unity.Agar/Server/Gateway/Gateway.csproj --no-restore`, but
   that gateway project is not part of the root `Lakona.slnx`. A clean checkout
   that restores and builds only the root solution will not necessarily have
   `samples/Game.Unity.Agar/Server/Gateway/obj/project.assets.json`, so the
   test can pass on a developer machine that previously restored the sample and
   fail on a fresh CI agent.

   Fix by either adding the Unity Agar gateway project to the normal
   restore/build validation path, running an explicit restore for that sample
   before the `--no-restore` build, or letting this specific E2E test run
   `dotnet build` without `--no-restore`. The important property is that the
   test must not depend on stale `obj` contents from a previous local build.

## Open Implementation Choices

These choices should be resolved during implementation with small tests:

- Whether user lifecycle hooks are one broad interface or several focused
  single-method interfaces.
- Whether connection-id reverse lookup belongs directly in
  `IGameSessionDirectory` or in a companion tracker owned by the session
  package.
- Whether the hotfix generator shares RPC naming/model code through a small
  common internal helper or relies on locked naming tests.
- Whether generated `UseGeneratedHotfixServices()` should live as an extension
  on `LakonaGameServerBuilder` or as a lower-level binder called by a framework
  extension.

Do not defer these as compatibility decisions. Pick the smallest design that
keeps the boundary explicit and update this document if implementation proves a
different shape cleaner.
