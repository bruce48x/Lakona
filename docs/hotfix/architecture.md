# Hotfix Architecture

## Purpose

Hotfix is a required part of Lakona server authoring. It is not an optional
extension and generated or sample game servers must not provide a non-hotfix
business-logic path.

Hotfix lets a running game server replace request logic and actor business
behavior without restarting the process.

The model is:

```txt
stable runtime state + replaceable business logic
```

Long-lived state, actor mailboxes, RPC transport, session ownership,
persistence, logging, timers, and process lifecycle stay in stable assemblies.
Replaceable business logic lives in `Server.Hotfix` and is loaded through a
collectible `AssemblyLoadContext`.

## Core Concepts

Lakona uses Actor terminology, not ECS terminology. Public hotfix names must not
use Entity, Component, or System for the game-facing model.

| Concept | Assembly | Example | Responsibility |
| --- | --- | --- | --- |
| Service contract | `Shared` | `IChatService` | RPC interface shared by client and server |
| Service | `Server.Hotfix` | `ChatService` | Request business logic for a Shared service contract |
| Lifecycle | `Server.Hotfix` | `ChatSessionLifecycle` | Replaceable business reaction to framework-owned lifecycle events |
| Feature descriptor | `Server.Hotfix` | `BattleRuntimeFeature` | Reloadable game feature declaration and LakonaTimer-backed feature timers |
| Actor | `Server.App` | `ChatRoomActor` | Stable mailbox, fields, actor identity, actor DTOs, and stable infrastructure dependencies only |
| Behavior | `Server.Hotfix` | `ChatRoomBehavior` | Hot-reloadable behavior for one actor type |
| Service proxy | `Server.App` | `ChatServiceProxy` | Stable RPC binding that forwards each call to current hotfix service logic |

The Service, Lifecycle, and Behavior concepts are deliberately separate:

- A Service corresponds to a `Shared` service interface. It handles request
  business logic and may call zero, one, or many actors.
- A Lifecycle corresponds to a framework-owned runtime lifecycle contract, such
  as game session disconnect or expiration. It handles replaceable business
  reactions without adding app-owned RPC lifecycle subscriptions.
- A Feature Descriptor names a user-authored game capability and declares
  feature lifecycle and timer callbacks for the stable scheduler.
- A Behavior corresponds one-to-one with an Actor. It runs inside an actor turn
  and reads or writes that actor's fields.
- A Service must not be named `*Behavior`.
- A Lifecycle must not be named `*LifecycleService`.
- A Behavior must not become an RPC endpoint.

## Project Structure

```txt
Server.App (stable)                         Server.Hotfix (reloadable)
────────────────────                        ──────────────────────────
Program entry point                         ChatService
RPC service proxies                         ChatRoomBehavior
Framework lifecycle bridge                  ChatSessionLifecycle
Actor fields and mailbox ownership          Service helpers
Hotfix dispatch bridge                      Request orchestration
Local admin hotfix route modules            Replaceable rules
Timer scheduler                             Hotfix feature descriptors
BuildTag metadata

Reference direction: Server.Hotfix -> Server.App and Shared
```

`Server.App` must not reference `Server.Hotfix`. It loads the hotfix assembly
dynamically through `HotfixManager`. No host, sample, tool template, or feature
discovery path may load hotfix assemblies with `Assembly.LoadFrom` into the
default `AssemblyLoadContext`.

Every supported Lakona server project has both a stable `Server.App`
project and a reloadable `Server.Hotfix` project. Framework code, samples, and
tool output must not include an alternate "hotfix disabled" project shape.

## Request Flow

Each RPC session holds stable proxy instances, not hotfix service instances.
This guarantees already-connected clients use the newest service logic on their
next RPC call after a successful reload.

```txt
client RPC
  -> Server.App ChatServiceProxy
  -> current hotfix ChatService
  -> generated ChatRoomActors.Get/Local/Remote selector
  -> current ChatRoomBehavior inside the actor turn
  -> mutate ChatRoomActor fields
  -> return stable DTO/effects to the proxy/runtime
```

Existing calls use next-entry semantics. A call that already resolved a delegate
continues with that delegate. New proxy calls and new actor behavior calls see
the new dispatch table after a successful reload.

## Actor And Behavior Boundary

Actors are stable state holders and mailbox identities. User actor classes in
Lakona server projects must contain state fields, stable infrastructure
dependencies, and framework lifecycle hooks only. Business decisions belong in
the matching Behavior.

```csharp
// Server.App
internal readonly record struct ChatRoomId(string Value);

internal sealed class ChatRoomActor : Actor<ChatRoomId>
{
    internal readonly Dictionary<string, ChatRoomMember> Members = new();
    internal readonly Queue<ChatMessage> RecentMessages = new();
}
```

```csharp
// Server.Hotfix
[HotfixBehaviorOf(typeof(ChatRoomActor))]
internal static partial class ChatRoomBehavior
{
    public static ValueTask<LoginReply> LoginAsync(
        this ChatRoomActor self,
        ChatLoginCommand command)
    {
        // Reads and writes ChatRoomActor fields inside the actor turn.
    }
}
```

Behavior field access should use `internal` access with
`InternalsVisibleTo("Server.Hotfix")` or generated friend accessors. It should
not use runtime reflection for normal actor field access.

Public extension methods in `[HotfixBehaviorOf]` classes are the actor API.
Stable `Server.App` owns actor state and actor DTOs. `Server.Hotfix` owns the
behavior-derived actor selectors, refs, and dispatch wrappers that expose those
methods to service and lifecycle code.

Hotfix code must not own long-lived timers, threads, static event
subscriptions, cached callbacks, or any object that can keep an old collectible
load context alive.

The detailed authoring rules are defined in
[actor-behavior.md](actor-behavior.md). If another document appears to allow
user-authored business methods on `Server.App` actor classes, this hotfix
boundary takes precedence.

## Service Proxy Boundary

Stable service proxies are the RPC binding surface. They implement the `Shared`
contract and forward every call to the currently loaded hotfix Service through
the hotfix dispatch layer.

```txt
Shared.IChatService
  implemented by Server.App.ChatServiceProxy
  forwarded to Server.Hotfix.ChatService
```

This prevents RPC registries and existing sessions from holding instances of
types loaded from the hotfix assembly. It is required for old connections to use
new service logic after reload and for old hotfix load contexts to unload.

Hotfix service and lifecycle implementation classes may use constructor
injection. The dispatcher activates one fresh instance per non-static hotfix
service or lifecycle method call using the current generation provider carried
by the call context. The dispatcher disposes that instance after the returned
`ValueTask` completes.

Hotfix service and lifecycle implementation classes are not registered in the
stable root DI container. Dependencies registered by `HotfixFeatureContext`
belong to the current hotfix generation; stable framework dependencies are
resolved through the provider fallback to the root container.

High-frequency service methods may remain static when avoiding one service
instance allocation per request is required. Static methods may resolve the
small set of required dependencies directly from `call.Services` in local
variables. Do not hide those lookups behind dependency records whose only
purpose is DI resolution.

Framework-owned lifecycle bridges use the same dispatch boundary. Stable app
code enables the framework bridge, and the current hotfix assembly provides one
`[HotfixLifecycle(typeof(TContract))]` implementation for the required
lifecycle contract. The bridge invokes lifecycle methods through explicit
`[RpcMethod]` ids; generated and sample app code must not add user-authored raw
RPC lifecycle subscriptions or app-local lifecycle bridge classes.

## Hotfix Feature Descriptors

Stable `LakonaGameFeature` is framework infrastructure. User-authored game
feature declarations live in the hotfix assembly:

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .CreateAsync<MatchmakingActor>(ActorId.From("default"), call.CancellationToken);

        var timerId = await LakonaTimer.CreatePeriodicTimerAsync<BattleRuntimeTimers, BattleRuntimeTick>(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50),
            nameof(BattleRuntimeTimers.TickAsync),
            new BattleRuntimeTick("default"),
            call.CancellationToken);

        call.State.Items["battle-runtime.timer"] = timerId;
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .DestroyAsync<MatchmakingActor>(ActorId.From("default"), CancellationToken.None);

        if (call.State.Items.TryGetValue("battle-runtime.timer", out var value) &&
            value is TimerId timerId)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None);
        }

        call.State.Items.Remove("battle-runtime.timer");
    }
}

public sealed record BattleRuntimeTick(string QueueId);
```

The stable timer scheduler resolves callback names against the current hotfix
behavior table. Feature-owned timers are created from `StartAsync`, the
returned `TimerId` is stored in `HotfixFeatureState`, and `StopAsync` destroys
the timer before removing the state entry. Stop cleanup must not be skipped only
because the stop request token was canceled; use a noncancelable cleanup token
when deleting feature-owned timers. Stable App code must not define
application-specific hotfix event adapters, room runtimes, matchmaking hosted
services, or game Feature classes.

`StartAsync` and `StopAsync` are activation and removal hooks, not every-reload hooks.
A successful reload that retains the same feature name
preserves `HotfixFeatureState` and does not rerun `StartAsync` or `StopAsync`
for that retained feature. `HotfixFeatureState` may store stable values such as
framework ids, timer ids, strings, primitive values, and DTOs from shared
non-collectible assemblies. It must not contain hotfix-owned DTOs, services, delegates, or instances
because those values can keep the collectible hotfix load context alive.

Feature descriptors are scanned and validated with the rest of the hotfix
assembly. They are not retained as long-lived runtime objects from the
collectible hotfix load context. After a successful reload, the framework
publishes the new hotfix provider, dispatch table, behavior table, lifecycle
handlers, and timer registrations as one generation.
If scanning or validation fails, the previous generation remains active.

### Feature Commands

Hotfix feature declarations use `public static void Configure(HotfixFeatureContext context)`.
Static `Configure` is the declaration surface for discoverability, metadata,
hotfix-generation services, local actor declarations, LakonaTimer-backed
feature timers, and typed feature commands. The scanner rejects missing
`Configure`, instance `Configure`, and any public `Configure` method with a
different signature; descriptors must declare exactly one
`public static void Configure(HotfixFeatureContext context)` and no other public `Configure` overloads.
The scanner does not construct feature classes
during declaration.

Runtime feature command calls activate a fresh feature instance from the current
hotfix service provider, invoke a method shaped as
`ValueTask<TReply> Method(HotfixFeatureCommandCall<TRequest> call)`, and dispose
the feature instance after the returned `ValueTask` completes.
`HotfixFeatureCommandCall<TRequest>` carries the request DTO, feature name,
`FeatureCommandId`, correlation id, source node, expiration timestamp,
cancellation token, and current generation service provider.

Feature command request and reply DTOs may be hotfix-owned types. Their wire
compatibility is governed by the hotfix BuildTag and the active hotfix generation,
not by the stable RPC service boundary validator.

Feature command request and reply payloads use the configured
`Lakona:Cluster:Serializer` because feature commands travel over the
node-to-node cluster channel. If that serializer is `memorypack`, hotfix-owned
command DTOs must be MemoryPack-serializable in the current hotfix generation.

The stable cluster boundary owns the low-level `IFeatureMessageHandler`. It
parses `FeatureMessageRequest.Kind` as an invariant-culture positive
`FeatureCommandId`, rejects blank, null, non-integer, zero, negative, or
overflow values with `ClusterSendStatus.Rejected`, and dispatches valid
commands into the current hotfix feature command table. Generated projects,
samples, and ordinary hotfix business code must not register hotfix-side
`IFeatureMessageHandler` implementations.

Caller cancellation propagates through `HotfixFeatureCommandCall<TRequest>`.
If the caller-provided cancellation token is canceled during dispatch, the
handler propagates `OperationCanceledException` to the local caller. If a
command throws `OperationCanceledException` without that token being canceled,
the failure is treated as a command failure and maps to
`ClusterSendStatus.Failed`.

## Generated Hotfix Services

`Lakona.Game.Server.Hotfix.Generators` owns stable hotfix service proxy
generation for `Server.App`.

Generated projects should use shared `[RpcService]` contracts as the source of
truth. When a user adds a supported shared service contract and implements a
matching hotfix `[HotfixService]`, the stable server app should not require a
hand-written proxy file, binder configurator, or service endpoint marker.

For each supported shared service contract, generated stable code provides:

- a proxy implementing the shared RPC service interface
- one method implementation per `[RpcMethod]`
- callback proxy construction when the shared contract declares a notification
  contract
- service binding through generated endpoint catalog binders
- an `IHotfixRequiredServiceContracts` provider discovered automatically by
  `LakonaGameServer.RunAsync`

Generated projects must not contain user-authored endpoint marker files such as
`GeneratedServiceEndpoints.cs`, and generated hotfix binding must not introduce
`EndpointName` or `GameEndpointName` as session identity.

The hotfix project validates the replaceable side of the contract. For every
generated hotfix-backed shared RPC service, there must be exactly one matching
`[HotfixService(typeof(TContract))]` implementation in the hotfix assembly. A
missing or duplicate hotfix service is a build or check failure.

## Hotfix Call Context

Hotfix service code accepts a framework call context rather than a project-local
call record.

The call context exposes stable runtime dependencies and the current RPC
connection id. It must not expose endpoint names:

```csharp
public interface IHotfixCallContext
{
    IServiceProvider Services { get; }
}

public class HotfixServiceCall<TRequest> : IHotfixCallContext
{
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
    public TCallback Callback { get; }
}

public sealed class HotfixLifecycleCall<TRequest> :
    HotfixServiceCall<TRequest>
{
}
```

RPC services and actor behaviors use generated behavior-first actor selectors
for ordinary business actor calls. `Get(id)` is the default service path and
resolves local or remote placement through the actor directory. `Local(id)` is
reserved for code that has already proven current-node ownership. `Remote(nodeId,
id)` pins a specific target node.

Hotfix-generated behavior-first actor selectors and refs are derived from
public methods on `[HotfixBehaviorOf]` classes and expose the behavior API at
service and lifecycle call sites. Stable runtime services, actor metadata, and
the stable cluster handler provide the cross-node dispatch boundary for route
lookup, local dispatch, remote dispatch, serialization, and actor call error
mapping.

`HotfixServiceCall.Actors` and raw `IActorRuntime.AskAsync` / `TellAsync`
remain framework-level escape hatches. Samples and generated projects must not
use them as the normal business authoring style.

Service and lifecycle constructors receive long-lived dependencies through DI.
Method arguments carry request-specific data: request DTOs, connection id,
callback proxy, actor runtime, and game server APIs. Do not resolve ordinary
constructor dependencies manually inside method bodies. The exception is a
documented high-frequency static method that avoids service instance
allocation.

Return mapping stays one-to-one with the shared RPC contract:

- A contract method returning `ValueTask<TResult>` maps to a hotfix method
  returning `ValueTask<TResult>`.
- A contract method returning `ValueTask` maps to a hotfix method returning
  `ValueTask`.

The hotfix dispatch key must use the stable RPC method id from `[RpcMethod]`,
not the C# method name.

Instance service methods must use `HotfixServiceCall<TRequest>` or
`HotfixServiceCall<TRequest, TCallback>`. Instance lifecycle methods must use
`HotfixLifecycleCall<TRequest>`. Static service methods may continue to accept
raw request DTO parameters for allocation-sensitive paths. The scanner rejects
instance/raw-DTO dispatch and wrapper mismatches so service and lifecycle
contracts cannot be accidentally crossed.

## BuildTag

`BuildTag` is the stable hotfix compatibility tag. It proves a hotfix package
was built against the same stable boundary as the running server.

The tag is explicitly managed. It must not change automatically on every build.
Update it only when the stable boundary visible to hotfix code changes:

- actor fields are added, removed, renamed, or retyped
- `Shared` service contracts or DTOs change
- hotfix dispatch or generated wrapper shape changes
- hotfix-visible internal stable types change incompatibly

Do not update it for pure hotfix logic changes, comments, docs, tests, or stable
implementation details that are invisible to hotfix code.

Recommended storage:

```txt
Server/App/BuildTag.props
```

```xml
<Project>
  <PropertyGroup>
    <LakonaHotfixBuildTag>20260629.001</LakonaHotfixBuildTag>
  </PropertyGroup>
</Project>
```

`Server.App` and `Server.Hotfix` import this file. `Server.App` exposes the
running `BuildTag` through assembly metadata and the loopback hotfix admin
status endpoint. `lakona-tool hotfix pack` writes the same tag into
`hotfix.json`. Production activation rejects packages whose `BuildTag` does not
match the running server.

## Development Workflow

Development optimizes for speed.

```txt
dotnet build Server/Hotfix/Server.Hotfix.csproj
  -> copy Server.Hotfix.dll, PDB, and deps to Server.App output hotfix directory
  -> write reload.signal last
  -> development server detects reload.signal
  -> HotfixManager.ReloadAsync()
```

Development may use a signal watcher with a lightweight polling fallback.
It must watch `reload.signal`, not the DLL itself, so the server does not load a
partially copied build output.

Development reload failures are logged and keep the previous dispatch table.
They may be warnings during local iteration.

## Production Workflow

Production optimizes for reliability. It does not use file watchers.
Production hotfix package mode and local admin listener enablement are
independent.

Normal v1 flow:

```txt
build or CI machine:
  lakona-tool hotfix pack

external deployment system:
  copy the package to each target node

target node:
  lakona-tool hotfix install Server.Hotfix-v20260612-153045Z.zip --root /app/hotfix
  lakona-tool hotfix activate v20260612-153045Z --server http://127.0.0.1:20090
  lakona-tool hotfix status --server http://127.0.0.1:20090
```

Lakona v1 does not provide remote deploy or multi-node orchestration. Operators
or deployment systems roll nodes by invoking the local commands on each node.

Production mode selects the active hotfix from `hotfix/versions` through the
`current.txt` version pointer. If the local admin listener is disabled, startup
can still read `current.txt` and load the selected hotfix version. Online
`activate`, `status`, `rollback`, and `reload` operations are unavailable until
loopback local admin is explicitly enabled.

Production hotfix root:

```txt
hotfix/
  current.txt
  previous.txt
  staging/
  versions/
    v20260612-153045Z/
      Server.Hotfix.dll
      Server.Hotfix.pdb
      Server.Hotfix.deps.json
      hotfix.json
      checksums.sha256
      READY
```

`READY` is written last. A version directory without `READY` is not installable
or activatable.

Package names and version directories use UTC timestamps accurate to seconds:

```txt
Server.Hotfix-v20260612-153045Z.zip
v20260612-153045Z
```

## Local Admin Endpoint

Hotfix operations are route modules under Lakona's loopback local admin host.
They do not own a separate listener.

Production activation is explicit and local. The v1 local admin host:

- uses loopback HTTP JSON
- binds only to `127.0.0.1` or `::1`
- rejects non-loopback requests
- has no public authentication model
- is not a remote deploy channel

Required v1 endpoints:

```txt
GET  /_lakona/hotfix/status
POST /_lakona/hotfix/activate
POST /_lakona/hotfix/rollback
POST /_lakona/hotfix/reload
```

`activate` validates the target version in the running server process before it
publishes a new dispatch table:

```txt
1. acquire hotfix operation lock
2. verify version directory, READY, manifest, checksums, and BuildTag
3. dry-load the hotfix assembly without publishing
4. verify expectedCurrentVersion
5. write previous.txt = old current version
6. write current.txt = target version
7. call ReloadAsync()
8. on success, return current status
9. on failure, restore current.txt and keep old dispatch table
```

`rollback` activates `previous.txt`. It is ordinary activation of the previous
version, not a separate loading path.

`reload` reloads the version already named by `current.txt` and does not change
version pointers.

## Dispatch Publication Safety

Reload failure keeps the previous dispatch table active. A reload is successful
only after all of these checks pass:

- the resolved DLL exists and can be read completely
- the assembly loads in a collectible context
- scanning finds supported hotfix Service and Behavior methods
- duplicate dispatch keys are rejected
- boundary types come from shared/default assemblies
- `BuildTag` matches in production
- typed delegates for supported dispatch shapes can be created
- no hotfix assembly was loaded in the default context by the host

## Explicit Non-Goals

V1 does not include:

- remote upload or deploy from `lakona-tool`
- multi-node orchestration
- public management endpoints
- production file watchers
- hotfixing actor runtime internals
- hotfixing serializers, transports, or persistent schema
- hotfixing `Shared` contract shape without a stable deployment and `BuildTag`
  bump
- allowing hotfix code to own long-lived runtime resources
