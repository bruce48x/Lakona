# Generated Hotfix Services And Session Lifecycle

Status: target design update
Date: 2026-06-15
Audience: maintainers and implementation agents

Implementation plan: [One-RPC-Session Lifecycle And Generated Hotfix Services Implementation Plan](./generated-hotfix-services-and-session-lifecycle-implementation-plan.md).

This document supersedes the earlier endpoint-name based design. The new model
keeps Lakona.Game responsible for one RPC session at a time and leaves
multi-session aggregation to user code.

## Purpose

Generated Lakona.Game projects should not require users to hand-write stable
RPC service proxies, binder configuration, service endpoint marker files, or raw
`RpcSession` disconnect tracking when they add a new service.

The long-term model is:

```txt
Shared RPC contract
  -> generated stable Server.App proxy and binding
  -> current Server.Hotfix service method
  -> framework-owned single RPC session lifecycle
  -> user-owned account/player/character/session aggregation
```

The framework is still early. Implementation should favor a clean long-term
shape over compatibility with the current sample code.

## Non-Negotiable Decisions

1. A `GameSessionKey` represents exactly one game RPC session.
2. One bound RPC connection is associated with at most one `GameSessionKey`.
3. One game RPC session is associated with at most one active RPC connection at
   a time.
4. Multiple game RPC sessions for the same account, player, character, or room
   are user-managed business state.
5. `EndpointName` and `GameEndpointName` are not user-facing concepts in the
   hotfix service generator, `ILakonaGameServer`, reliable push APIs, or session
   directory.
6. `Lakona.Game:Endpoints[]` remains transport configuration. It is used to host
   RPC listeners, not to create framework session sub-identities.
7. The framework must not invent hidden names such as `control`, `realtime`, or
   `default` for session bookkeeping.
8. Source generators must remove the need for
   `Server/App/Services/GeneratedServiceEndpoints.cs`.

## Current Problem

The current generated and sample shape still exposes two pieces of template
code that users should not own:

```txt
Server/App/Services/GeneratedServiceEndpoints.cs
Server/App/Hosting/ServiceBindingConfigurator.cs
```

The current marker file contains declarations like:

```csharp
[HotfixRpcService(typeof(ILoginService), EndpointName = "control")]
internal static partial class LoginServiceEndpoint;
```

That shape has two problems:

- `EndpointName = "control"` is not configured anywhere in
  `Lakona.Game:Endpoints[]`; generated configuration distinguishes listeners by
  `Transport`.
- The marker file is mechanically derived from shared `[RpcService]` contracts,
  so users must edit framework glue when they add a service.

The current session model also stores callbacks by
`GameSessionKey + GameEndpointName + callback contract type`. That makes the
framework responsible for grouping a player's control and realtime channels.
In production, those channels are often hosted by different processes or node
pools, so process-local callback aggregation under one `GameSessionKey` is the
wrong abstraction.

## Vocabulary

| Term | Meaning |
| --- | --- |
| RPC connection | One accepted transport connection owned by `Lakona.Rpc.Server`. |
| Connection id | Stable id for the RPC connection, currently aligned with `RpcSession.ContextId`. |
| Game session | One framework-owned logical RPC session, identified by `GameSessionKey`. |
| Session callback binding | A `GameSessionKey + callback contract type -> connection id + callback` record. |
| Business session group | User-owned mapping such as `PlayerId -> control session + realtime session`. |
| Transport endpoint | A configured listener in `Lakona.Game:Endpoints[]`, identified by transport in the current schema. |
| Business presence | Product-specific state such as room membership, lobby presence, or character online state. |

Do not use `control`, `realtime`, or `endpoint name` as framework session
vocabulary. Those are business or deployment concepts.

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

The stable server project should not contain a service endpoint marker file.
There should be no user-authored equivalent of:

```txt
Server/App/Services/GeneratedServiceEndpoints.cs
```

`Program.cs` binds generated hotfix-backed services through one generated or
framework-facing extension:

```csharp
return await LakonaGameServer.RunAsync(args, server => server
    .UseTransport("websocket")
    .UseSerializer(() => new MemoryPackRpcSerializer())
    .UseAcceptor(async opts => await WsConnectionAcceptor.CreateAsync(
        opts.Port,
        opts.Path,
        opts.Host))
    .UseGeneratedHotfixServices());
```

When a user adds a new shared `[RpcService]` interface and implements a matching
hotfix `[HotfixService]`, no stable proxy file, binding configurator, endpoint
marker, or endpoint name should be written by hand.

## Source Generation Model

`Lakona.Game.Server.Hotfix.Generators` owns stable hotfix service proxy
generation for `Server.App`.

The `Server.App` generator should discover supported user shared
`[RpcService]` interfaces from the current compilation and metadata references.
In generated projects, those contracts come from the shared contract project
referenced by `Server.App`.

For the first implementation, every supported `[RpcService]` contract declared
outside Lakona framework assemblies and visible to `Server.App` is considered
hotfix-backed. Skip contracts whose containing assembly name starts with
`Lakona.`. If a project later needs stable non-hotfix services, add an explicit
opt-out or inclusion mechanism as a separate design; do not reintroduce
per-service marker files for the default path.

For each supported shared RPC service contract, generate:

- an internal stable proxy implementing the shared RPC service interface
- one method implementation per `[RpcMethod]`
- construction of `HotfixServiceCall<TRequest>` or
  `HotfixServiceCall<TRequest, TCallback>`
- callback proxy construction when the shared contract declares
  `NotificationContract`
- service binding that uses the generated RPC binder for the contract
- a generated extension such as `UseGeneratedHotfixServices`

The generator must not require `[HotfixRpcService]` marker declarations in
`Server.App`. If the current implementation keeps `HotfixRpcServiceAttribute`
temporarily, the migration is not complete until generated projects no longer
emit or require it.

The generator must not generate `EndpointName`, `GameEndpointName`, or
binding-set code. Binding to a particular RPC listener is a host composition
concern. In the default generated project, `UseGeneratedHotfixServices()` binds
the generated services to the single configured listener.

For future same-process multi-transport hosts, service-to-listener selection
should be expressed through explicit host composition or Feature registration,
not through session identity. A transport may decide which generated binder set
to install, but that transport choice must not become part of `GameSessionKey`
or callback storage.

The generator must reject unsupported service shapes with diagnostics:

- the contract type is not an interface marked `[RpcService]`
- an RPC method lacks `[RpcMethod]`
- an RPC method does not have exactly one request DTO parameter
- an RPC method does not return `ValueTask` or `ValueTask<TResult>`
- callback metadata is inconsistent with the generated RPC service model
- duplicate shared service contracts would produce duplicate generated bindings

The hotfix project must validate the other side of the contract. For every
generated hotfix-backed shared RPC service, there must be exactly one matching
`[HotfixService(typeof(TContract))]` implementation in the hotfix assembly. A
missing or duplicate hotfix service is a build or check failure, not a runtime
surprise.

The stable generator should expose the discovered hotfix-backed contract list
through a generated required-contract provider consumed by `HotfixManager`
during validate and reload. `HotfixManager` should not independently scan
`Server.App` to infer that list.

The generator must not parse generated source text from `Lakona.Rpc.Analyzers`.
Implementation should either share a small RPC service model/naming helper or
define stable generated-symbol contracts that both generators can rely on.

## Hotfix Dispatch Contract

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
            call.ConnectionId,
            call.Callback);

        return new LoginReply(session);
    }
}
```

The call context exposes stable runtime dependencies and the current RPC
connection id. It must not expose `EndpointName` or `GameEndpointName`:

```csharp
public class HotfixServiceCall<TRequest>
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId;
        Services = services;
        Actors = actors;
        GameServer = gameServer;
    }

    public TRequest Request { get; }
    public string ConnectionId { get; }
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
        TCallback callback,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : base(request, connectionId, services, actors, gameServer)
    {
        Callback = callback;
    }

    public TCallback Callback { get; }
}
```

Return mapping stays one-to-one with the shared RPC contract:

- A contract method returning `ValueTask<TResult>` maps to a hotfix method
  returning `ValueTask<TResult>` and a generated proxy call to
  `IHotfixServiceInvoker.InvokeAsync<TContract, TCall, TResult>`.
- A contract method returning `ValueTask` maps to a hotfix method returning
  `ValueTask` and a generated proxy call to
  `IHotfixServiceInvoker.InvokeAsync<TContract, TCall>`.

The hotfix dispatch key must use the stable RPC method id from `[RpcMethod]`,
not the C# method name. The shared RPC contract is the source of truth for wire
compatibility.

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
            _callback,
            _services,
            _actors,
            _gameServer));
}
```

## Game Session Semantics

`GameSessionKey` identifies one framework-owned game RPC session. It is not the
same thing as an account, player, character, room member, device, or transport
channel group.

Starting a new session for an owner must not automatically invalidate other
sessions for the same owner. `OwnerKey` is a user-provided ownership label for
diagnostics, authorization, lookup, or user-maintained indexing. It is not a
framework uniqueness constraint.

`Generation` may remain part of `GameSessionKey`, but it is a version for that
specific session key, not a pointer to the owner's only current session.
Implementations may allocate it monotonically per owner for diagnostics and
resume validation, but they must not use it to reject other live sessions with
the same `OwnerKey`.

If a game wants only one active session per account, it must implement that
policy explicitly in user code, for example by storing
`AccountId -> GameSessionKey` and terminating or rejecting older sessions. If a
game wants both a WebSocket control session and a KCP realtime session for one
character, it stores that grouping itself:

```txt
CharacterId
  -> ControlSession: GameSessionKey
  -> RealtimeSession: GameSessionKey
```

This keeps the framework local and process-friendly. It does not require a
gateway process and a realtime process to share process-local callback objects
under one framework session record.

## Session Directory Semantics

The session directory should store sessions by `GameSessionKey`, not by current
owner.

Each session stores callback bindings by callback contract type:

```txt
GameSessionKey
  -> ILoginCallback: connection id + callback + state
  -> IChatCallback: connection id + callback + state
```

Binding a callback contract for a session replaces only that callback contract.
Binding `ILoginCallback` must not overwrite `IChatCallback`. Rebinding
`IChatCallback` updates that callback's connection id, callback instance, bound
timestamp, and disconnected state.

Binding a different active `GameSessionKey` to a connection id that already has
an active session binding is invalid. User code must explicitly terminate or
expire the old session before reusing that RPC connection for a different game
session.

For lifecycle publication, a session is considered active for a connection when
at least one callback binding for that session is active on that connection.
Adding a second callback contract for the same session and connection must not
publish a second session-bound event.

The session directory or a companion tracker must support connection-id lookup
so the RPC lifecycle bridge can mark sessions disconnected when an RPC
connection closes.

Suggested directory operations:

```csharp
ValueTask<GameSessionBindResult> BindSessionAsync<TCallback>(
    GameSessionKey session,
    string connectionId,
    TCallback callback,
    CancellationToken cancellationToken = default)
    where TCallback : class;

ValueTask<GameSessionSnapshot?> MarkConnectionDisconnectedAsync(
    string connectionId,
    CancellationToken cancellationToken = default);

ValueTask<IReadOnlyList<GameSessionSnapshot>> ExpireDisconnectedSessionsAsync(
    DateTimeOffset disconnectedBefore,
    CancellationToken cancellationToken = default);
```

Do not keep `SessionEndpointKey` in the target design. A game session no longer
has framework-owned endpoint children.

## ILakonaGameServer API Shape

Remove `GameEndpointName` parameters from user-facing game server APIs.

Target shape:

```csharp
public interface ILakonaGameServer
{
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<long> PublishReliablePushAsync<TCallback, TPayload>(
        GameSessionKey session,
        string kind,
        TPayload payload,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}
```

Exact method names can change during implementation, but the public model must
not require users or generated hotfix proxies to pass endpoint names.

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

`Lakona.Game.Server` registers an observer that turns RPC lifetime into game
session lifetime:

```txt
RPC session started
  -> game connection opened
  -> optional user lifecycle hooks

RPC session disconnected
  -> mark the game session bound to that connection disconnected
  -> publish one session-disconnected lifecycle hook
  -> cleanup later expires stale disconnected sessions
  -> publish one session-expired lifecycle hook when cleanup removes it
```

Session disconnection and session expiration are not the same as explicit
termination:

- Disconnection means the current RPC connection was lost and the session may
  still resume before retention expires.
- Expiration means disconnected session state was removed by cleanup policy.
- Termination means an explicit framework operation invalidated the session and
  optionally published a terminal notice.

## User Lifecycle Hooks

User hooks should receive game-level context, not `RpcSession` and not endpoint
names:

```csharp
public interface IGameSessionLifecycleHandler
{
    ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionBoundAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default);
}
```

Separate smaller interfaces are acceptable if implementation shows that
single-method hooks compose better. The event names must stay session-oriented,
not endpoint-oriented.

Business presence policy belongs in these hooks. A chat project should remove a
member from a room on session expiration or explicit session termination, not
automatically on every transient disconnect unless it deliberately chooses that
policy.

Hook failures must be contained:

- Framework state transitions happen before hook invocation.
- A hook exception is logged and surfaced through diagnostics.
- One failing hook must not stop later hooks from running.
- Hooks receive cancellation tokens but must not block shutdown indefinitely.

## Transport Configuration

`Lakona.Game:Endpoints[]` remains the source of listener configuration:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws"
      },
      {
        "Transport": "kcp",
        "Host": "127.0.0.1",
        "Port": 20001
      }
    ]
  }
}
```

The transport value decides how a listener is hosted. It does not become a
session identity in the framework. If a game wants to remember that a specific
`GameSessionKey` came from a WebSocket or KCP listener, it stores that in its
own business presence model.

The first supported schema still disallows duplicate transports in one process.
If a future project needs two WebSocket listeners in one process, add an
explicit transport endpoint name to the configuration schema then. Do not
reintroduce hidden `EndpointName` strings in the session APIs.

## Generated Project Shape

New `Lakona.Tool` output should stop rendering stable proxy, binder, endpoint
marker, and raw RPC lifecycle files.

Remove from generated default projects:

```txt
Server/App/Chat/LoginServiceProxy.cs
Server/App/Chat/ChatServiceProxy.cs
Server/App/Hosting/ServiceBindingConfigurator.cs
Server/App/Services/GeneratedServiceEndpoints.cs
Server/App/Chat/ChatConnectionLifecycle.cs
```

Add or keep focused lifecycle policy only when the sample needs it:

```txt
Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs
```

Lifecycle hook registration should happen in normal startup composition:

```csharp
.AddServices(services =>
{
    services.AddLakonaGameServerSessionCleanup(options =>
    {
        options.DisconnectedSessionRetention = TimeSpan.FromSeconds(30);
    });
    services.AddSingleton<IGameSessionLifecycleHandler, ChatPresenceLifecycleHandler>();
})
```

Generated docs should teach three edit zones:

- `Shared/Contracts/**`: define service, callback, and DTO contracts.
- `Server/Hotfix/**`: implement hot-reloadable service and actor behavior logic.
- `Server/App/Lifecycle/**`: implement stable business lifecycle hooks such as
  presence cleanup.

There should be no generated-project edit zone for service endpoint markers.

## Interaction With Hotfix BuildTag

Generated service proxy shape, call context shape, and lifecycle hook contracts
are stable boundaries visible to hotfix code. Changes to those shapes require a
`BuildTag` update in generated projects.

Pure hotfix method body changes do not require a `BuildTag` update.

## Migration Instructions For Implementation

Make these changes as one coherent refactor. Do not leave compatibility layers
unless a test requires a temporary bridge inside the same PR.

1. Remove `GameEndpointName` from `ILakonaGameServer`, reliable push APIs,
   hotfix call contexts, generated hotfix proxies, and session directory APIs.
2. Replace `SessionEndpointKey` storage with direct `GameSessionKey` storage.
3. Change session storage from owner-singleton to session-keyed. Starting a new
   session for an existing owner must not replace the owner's previous session.
4. Rename endpoint lifecycle hooks and contexts to session lifecycle hooks.
5. Replace endpoint cleanup options with session cleanup options.
6. Rename endpoint closer abstractions to session or connection closer
   abstractions.
7. Remove `HotfixRpcServiceAttribute` marker usage from generated projects.
8. Generate stable hotfix proxies from shared `[RpcService]` contracts.
9. Delete `Server/App/Services/GeneratedServiceEndpoints.cs` from tool output
   and samples.
10. Update docs and package READMEs that mention endpoint binding or
   `GameEndpointName`.
11. Bump affected package versions under `src/**` before release.

## Testing And Validation Requirements

Implementation must add focused tests at these boundaries:

- `Lakona.Rpc.Server.Tests`: RPC session lifecycle observers receive started
  and disconnected events exactly once per accepted connection.
- `Lakona.Game.Server.Tests`: starting two sessions for the same owner leaves
  both sessions resumable until user code explicitly terminates one.
- `Lakona.Game.Server.Tests`: binding multiple callback contract types to one
  `GameSessionKey` does not overwrite unrelated callback bindings.
- `Lakona.Game.Server.Tests`: rebinding one callback contract updates only that
  callback contract.
- `Lakona.Game.Server.Tests`: binding a second active `GameSessionKey` to the
  same connection id is rejected.
- `Lakona.Game.Server.Tests`: a disconnected RPC connection marks the bound
  game session for that connection disconnected.
- `Lakona.Game.Server.Tests`: disconnected session cleanup expires stale
  sessions and publishes session-expired hooks.
- `Lakona.Game.Server.Tests`: lifecycle hook exceptions are logged or captured
  without preventing state transitions.
- `Lakona.Game.Server.Hotfix.Generators.Tests`: shared `[RpcService]`
  contracts generate stable hotfix proxies without marker declarations.
- `Lakona.Game.Server.Hotfix.Tests` or generator tests: every generated
  hotfix-backed shared RPC service requires exactly one matching
  `[HotfixService(typeof(TContract))]` implementation.
- `Lakona.Game.Server.Hotfix.Generators.Tests`: generated proxies construct
  `HotfixServiceCall` without endpoint arguments.
- `Lakona.Game.Server.Hotfix.Generators.Tests`: unsupported contracts produce
  diagnostics instead of invalid generated code.
- `Lakona.Tool.Tests`: generated projects no longer contain hand-written
  `*ServiceProxy.cs`, `ServiceBindingConfigurator.cs`,
  `GeneratedServiceEndpoints.cs`, or raw `RpcSession.Disconnected` tracking.
- Sample or tool E2E: a generated Godot or Unity project builds from a clean
  restore, starts after hotfix build output exists, logs in, sends a chat
  message, disconnects, and exercises lifecycle cleanup.

Source-scan tests should reject these patterns in generated projects:

```txt
class LoginServiceProxy
class ChatServiceProxy
ServiceBindingConfigurator
GeneratedServiceEndpoints
HotfixRpcService(
EndpointName
GameEndpointName
SessionEndpointKey
OnEndpointBound
OnEndpointDisconnected
OnEndpointExpired
RpcSession.Disconnected +=
```

They should allow those names only in migration tests that assert removed API
coverage or in explicitly historical documentation.

## Consistency Checklist

Before handing implementation off, verify these statements remain true:

- `EndpointName` is not required anywhere in user-authored generated-project
  code.
- `GameEndpointName` is not part of public game session, hotfix call, or
  reliable push APIs.
- `GameSessionKey` identifies one RPC session, not a player aggregate.
- One bound RPC connection maps to at most one `GameSessionKey`.
- Starting another session for the same owner does not invalidate existing
  sessions.
- Multiple sessions for one account, player, character, or room are user-owned
  business state.
- Transport configuration hosts listeners but does not define framework session
  sub-identities.
- Lifecycle hook names are session-oriented, not endpoint-oriented.
- Generated projects teach users to edit shared contracts, hotfix logic, and
  lifecycle hooks only.
