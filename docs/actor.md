# Actor Model

Lakona exposes one public actor API for game code:
`Lakona.Game.Server.Actors`. The runtime behind it uses an internal actor
kernel under `Lakona.Game.Server.Internal.ActorKernel`, but that namespace is
not a package, not a public API, and not something generated projects should
reference.

Actors are the recommended way to model long-lived mutable game state such as
rooms, players, lobbies, matchmaking queues, leaderboards, and schedulers. An
actor is a concurrency boundary. It is not an ECS entity, an ORM model, or a
transparent distributed object.

## Responsibility Split

```txt
actor kernel                         Lakona.Game.Server.Actors
--------------------------------     --------------------------------
Mailbox queue                        Game actor identity
Sequential dispatch                  Actor base class and context
Call/response slots                  IActorRuntime
Timers                               DI activation
Stop/drain lifecycle                 Remote actor calls
Diagnostics mechanism                Cluster routing
Backpressure metrics                 Message recording storage
Tell / Call process-local plumbing   Reliable push integration
Message interceptor hooks            Hotfix behavior dispatch
Actor lifecycle state                Service discovery
```

The actor kernel answers one question: how does a single actor execute safely?
Lakona's public actor layer answers the game-server questions: how do actors
participate in sessions, hotfix, diagnostics, and cluster routing?

## Stable Actor, Hotfix Behavior

Hotfix is mandatory for Lakona game servers. User-authored actor classes in
`Server.App` are stable state holders. Game decisions belong in matching
`Server.Hotfix` behavior classes.

```csharp
// Server.App
public readonly record struct RoomId(string Value);

public sealed class RoomActor : Actor<RoomId>
{
    internal readonly HashSet<string> Members = new(StringComparer.Ordinal);
}
```

```csharp
// Server.Hotfix
[HotfixBehaviorOf(typeof(RoomActor))]
public static partial class RoomBehavior
{
    public static ValueTask<JoinRoomReply> JoinAsync(
        this RoomActor self,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        self.Members.Add(request.PlayerId);
        return new ValueTask<JoinRoomReply>(new JoinRoomReply { Accepted = true });
    }
}
```

Public behavior extension methods are the actor API exposed through generated
selectors and refs. They execute inside an actor turn and may read and mutate
the actor's stable fields, but hotfix code must not own long-lived timers,
threads, static event subscriptions, or callbacks that can keep an old hotfix
assembly alive.

The detailed authoring rules are in
[hotfix/actor-behavior.md](hotfix/actor-behavior.md).

## Generated Actor Access

Generated actor APIs should expose typed accessors for local and routed
calls without asking business code to hand-write actor ids, route keys,
serializers, or reply-correlation plumbing.

```csharp
public sealed class RoomActors
{
    public RoomLocalRef Local(RoomId id);

    public RoomRouteRef Route(RoomId id);
}
```

Inside the same actor turn, call the actor instance directly. Across actor
boundaries, use the generated collection:

```csharp
await rooms.Route(roomId).CallAsync(RoomBehavior.JoinAsync, request, cancellationToken);
await rooms.Local(roomId).PostAsync(RoomBehavior.RunTickAsync, request, cancellationToken);
```

Selector semantics:

- `Route(id)` is the normal business path. It owns actor-directory lookup and
  node selection before dispatch.
- `Local(id)` invokes only the process-local actor runtime and should be used
  only after the caller has already proven current-node ownership.

Generated actor collections are call selectors only. They must not expose
lifecycle helpers such as `SpawnAsync`, `DestroyAsync`, or hidden hook-based
creation methods. Actor hosting is a separate operation owned by
`ActorHosting`.

Generated actor refs expose generic `CallAsync` and `PostAsync` helpers.
`CallAsync` is completion-aware and surfaces the behavior reply or a typed
actor call failure. `PostAsync` is acceptance-only and completes once the
mailbox or remote transport accepts the work. Lower-level status-returning
APIs remain available for framework internals and boundary services.

Remote actor request and reply payloads use the cluster serializer selected by
`Lakona:Cluster:Serializer`. They do not use the client-facing endpoint
serializer unless that endpoint happens to use the same serializer. The
actor-facing `IRemoteActorSerializer` abstraction defaults to an adapter over
the configured cluster `IRpcSerializer` when active cluster endpoint wiring is
used, so a project generated with `--serializer memorypack` uses MemoryPack for
remote actor payloads as well as cluster RPC payloads.

The default `RpcRemoteActorSerializer` is registered by active cluster endpoint
wiring, not by `AddLakonaGameServerActors()`. Direct
`AddLakonaGameServerActors()` usage is process-local: it installs the actor
runtime and local actor services, but it does not register a default
`IRemoteActorSerializer` or a cluster `IRpcSerializer`. Hosts that bypass the
normal game server or cluster endpoint wiring and still use generated
non-local actor references must explicitly register compatible remote actor
serialization, cluster routing, directory, and transport-client services.

A custom `IRemoteActorSerializer` can intentionally override the built-in
adapter, but then the project owns cross-node compatibility for every generated
remote actor request and reply payload. When the cluster serializer is
`memorypack`, those user-defined actor payload DTOs must be
MemoryPack-serializable.

## Actor Key Model

Actor key type is declared in the actor base type:

```csharp
public sealed class RoomActor : Actor<RoomId>
{
}
```

This avoids separate key attributes and avoids generator guessing. The
generator uses `TKey` to type `Local(TKey id)` and `Route(TKey id)`.

Default key-to-string conversion:

1. If `TKey` has a readable `Value` property, use `Value.ToString()`.
2. Otherwise use `TKey.ToString()`.

Default actor id shape:

```txt
<actor-name>/<key-value>
```

Long-lived protocols can pin the wire name and method ids with `[ActorName]`
and `[ActorMethod]`.

Actor ids are global, stable business ids. They must not encode node id,
transport endpoint, callback state, connection id, or RPC session objects.
Good ids are shaped around the business object:

```txt
user/player-123
matchmaking/default
room/room-456
leaderboard/current
```

`ActorHosting` owns route registration for actors it creates. User code should
not separately publish an actor route for a local actor created through
framework lifecycle APIs.

## Failure Model

Generated `CallAsync` operations return a reply on success and throw typed
exceptions on local or routed failure.

```csharp
try
{
    var reply = await rooms
        .Route(roomId)
        .CallAsync(RoomBehavior.JoinAsync, request, cancellationToken);
}
catch (ActorCallException ex) when (ex.Status == ActorCallStatus.ActorNotFound)
{
    // The room has gone away or was never registered.
}
```

Actor failures should carry structured details such as status, node, actor id,
actor name, method name, and correlation id. Initial status values should cover
route not found, expired route, timeout, backpressure, handler unavailable,
node unavailable, serialization failure, deserialization failure, and
cancellation.

## Actor Diagnostics Privacy

Default actor diagnostics expose aggregate actor type counts and mailbox
counters. They must not expose per-actor identity or request state.

Default diagnostics JSON, metric tags, and trace attributes must not include
actor ids, actor names, call chains, message payloads, request values, session
ids, tokens, or user-specific identifiers.

Allowed low-cardinality fields include actor type, message type, timeout
reason, mailbox queue totals, processed counts, rejected counts, and
slow-message counters.

Detail endpoints are disabled by default. Any endpoint that exposes more than
aggregate actor diagnostics requires explicit diagnostics detail mode.

## Runtime Layers

The generated typed API sits above existing cluster primitives:

```txt
game service code
  -> generated RoomActors.Local/Route refs
  -> ActorDirectory cache / local actor invoker / remote actor invoker
  -> IActorRuntime / IClusterRouter / IClusterNodeSender
  -> ClusterActorEnvelope
  -> ClusterMessage / RouteLocation / cluster serializer / transport adapter
```

Distributed actor traffic uses two routing planes. Business actor requests
resolve actor ownership through `IClusterRouter` and `IRouteDirectory`.
Framework control messages and replies that already carry a destination
`NodeId` use `IClusterNodeSender`, which resolves that node through
`INodeDirectory`.

The `reply/<node-id>` key carried by a reply message is only a local handler key
on the destination node. It is never registered in `IRouteDirectory` as a
cluster route. Reply correlations are likewise destination-local pending-call
state rather than cluster routing state.

`ActorDirectory` lives in `Lakona.Game.Server`. Business code should not
receive endpoint addresses or directory endpoint names.

The lower-level `ClusterMessage`, `ClusterActorEnvelope`, `IClusterRouter`,
`AskRemoteAsync`, `TellRemoteAsync`, and remote actor invoker APIs remain
important foundations. They are escape hatches, not the recommended daily
business API.

`IActorRuntime` is a generated-support and advanced local runtime API. It
remains public because generated actor refs, hotfix service boundaries, tests,
diagnostics, and framework integrations may live in user assemblies, but it is
process-local and not the recommended daily business API. Ordinary gameplay
code should use generated actor selectors so local and routed placement intent
remains visible at the call site.

## Managed Lifecycle

Actor creation and destruction are current-node framework lifecycle operations
exposed through `ActorHosting.CreateAsync`, `ActorHosting.EnsureAsync`, and
`ActorHosting.DestroyAsync`. `AskAsync`, `TellAsync`, generated actor refs, and
timer callbacks do not create actors.

`ActorHosting` is the only public actor lifecycle entry point. It owns the
transaction across local actor activation, `ActorDirectory`, and
`ActorDirectoryCache`. User code should not separately publish or clear actor
routes for actors created through `ActorHosting`.

Cross-node creation goes through the registered actor placement strategy. The
selected node calls `ActorHosting` on its own process.

Creation, placement, capacity, and idempotency belong at the actor placement
boundary. Once an actor exists, services and gateways should call ordinary
business behavior through generated actor refs. Raw `IActorRuntime.AskAsync`
and `TellAsync` remain framework-level escape hatches.

Lifecycle method semantics:

- `CreateAsync<TActor>` is strict. It fails if the actor id is already hosted
  locally, if the local id belongs to a different actor type, or if the
  directory reports an owner on another node.
- `EnsureAsync<TActor>` is idempotent only for an active local actor of exactly
  the requested type. It still fails for type mismatch, non-active local state,
  or a remote directory owner.
- `DestroyAsync<TActor>` is current-node cleanup. It is successful when the
  actor and local route are already absent, but it must not delete a route owned
  by another node or stop a local actor of a different type.
- `[ActorLocalOnly]` actors skip directory and cache work. Distributed actors
  publish or clear current-node routes as part of the hosting transaction.

Hosting failures are typed exceptions derived from `ActorHostingException`.
Important cases include `ActorAlreadyHostedException`,
`ActorHostingTypeMismatchException`, `ActorHostedElsewhereException`,
`ActorDirectoryUnavailableException`, and `ActorHostingStopException`.
Actor call exceptions remain separate; they describe failed calls to already
selected actors, not actor lifecycle operations.

Missing actor behavior is deterministic:

- `AskAsync`, `TellAsync`, and generated actor refs return or throw structured
  `ActorNotFound` failures.
- timer callbacks target existing actors through normal actor calls and report
  diagnostics when the actor is missing.
- no normal actor call path implicitly creates the actor.

Distributed actor destroy order is:

```txt
remove local route/cache -> drain mailbox -> deactivate -> remove local actor
```

Removing the route first stops new routing to the current node before the local
actor drains. If local stop fails after route removal, `ActorHosting` best-effort
restores the local route/cache before throwing. If another node owns the route,
`DestroyAsync` leaves that route intact and only removes stale current-node
cache/local actor state for the requested type.

## Timers

Hotfix timers are framework-owned callbacks created through `LakonaTimer`.
Actor startup is declared from a `[HotfixStartup]` type, while periodic work
should stay inside hotfix actor behavior or explicit timer callbacks:

```csharp
[HotfixStartup]
public static class GameHotfixStartup
{
    [HotfixConfigureActors]
    public static void Actors(ActorHostBuilder actors)
    {
        actors.RegisterStartup(
            "matchmaking",
            static _ => ActorStartupPlan.Create<MatchmakingActor>(ActorId.From("default")));
    }
}

public sealed record BattleRuntimeTick(string QueueId);

public sealed class BattleRuntimeTimers
{
    public static ValueTask TickAsync(TimerTick<BattleRuntimeTick> tick)
    {
        // Enter generated actor selectors or services here.
        return default;
    }
}
```

The method name is explicit on purpose. Use `nameof(...)` so the call site shows
which callback will run and normal refactoring tools keep the declaration in
sync. The scheduler stores the method name rather than a delegate because a
delegate could keep an old reloadable hotfix assembly generation alive after
reload.

## Analyzer Boundary

Analyzer rules apply across the actor and hotfix boundary:

| Rule | Scope |
| --- | --- |
| `ULA001` no self-call | actor kernel |
| `ULA002` no blocking wait | actor kernel |
| `ULA003` no discarded call | actor kernel |
| `ULGHOTFIX011` no actor business methods in stable app | hotfix authoring |

Future actor isolation or thread-safety rules should live in shared analyzer
packages when they affect both the kernel and the public game-facing facade.

## Configuration Flow

```txt
ActorRuntimeOptions
  -> actor kernel system options
     -> MailboxCapacity
     -> SlowMessageThreshold
     -> MessageInterceptor
  -> actor kernel spawn options
     -> MailboxCapacity
```

Lakona adds game-facing options on top, including call timeout, diagnostic
events, dead letters, slow message reporting, and call timeout handling.

When the actor kernel changes, `Lakona.Game.Server.Actors` adapts in the same
repository change. The kernel is not independently versioned.
