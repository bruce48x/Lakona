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
public static class RoomBehavior
{
    public static ValueTask<JoinRoomReply> JoinAsync(
        this RoomActor self,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        self.Members.Add(request.PlayerId);
        return new(new JoinRoomReply { Accepted = true });
    }
}
```

Behavior methods execute inside an actor turn. They may read and mutate the
actor's stable fields, but hotfix code must not own long-lived timers, threads,
static event subscriptions, or callbacks that can keep an old hotfix assembly
alive.

The detailed authoring rules are in
[hotfix/actor-behavior.md](hotfix/actor-behavior.md).

## Generated Actor Access

Generated actor APIs should expose typed accessors for local and distributed
calls without asking business code to hand-write actor ids, route keys,
serializers, or reply-correlation plumbing.

```csharp
public sealed class RoomActors
{
    public RoomRef Get(RoomId id);

    public RoomLocalRef Local(RoomId id);

    public RoomRemoteRef Remote(NodeId node, RoomId id);
}
```

Business code uses distributed access by default:

```csharp
var reply = await rooms
    .Get(roomId)
    .JoinAsync(request, cancellationToken);
```

Use explicit selectors when the placement matters:

```csharp
var localReply = await rooms
    .Local(roomId)
    .JoinAsync(request, cancellationToken);

var pinnedReply = await rooms
    .Remote(nodeId, roomId)
    .JoinAsync(request, cancellationToken);
```

Selector semantics:

- `Get(id)` checks the local runtime first, then resolves placement through
  `ActorDirectory`.
- `Local(id)` invokes only the process-local actor runtime.
- `Remote(nodeId, id)` sends to the specified node and does not query
  placement.

The business method surface should return normally or throw typed actor call
exceptions. Lower-level status-returning APIs remain available for framework
internals and boundary services.

## Actor Key Model

Actor key type is declared in the actor base type:

```csharp
public sealed class RoomActor : Actor<RoomId>
{
}
```

This avoids separate key attributes and avoids generator guessing. The
generator uses `TKey` to type `Get(TKey id)`, `Local(TKey id)`, and
`Remote(NodeId nodeId, TKey id)`.

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

The actor lifecycle service owns route registration for actors it creates. User
code should not separately publish an actor route for a local actor created
through framework lifecycle APIs.

## Failure Model

Generated business methods return a reply on success and throw typed exceptions
on local or distributed failure.

```csharp
try
{
    var reply = await rooms
        .Get(roomId)
        .JoinAsync(request, cancellationToken);
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

## Runtime Layers

The generated typed API sits above existing cluster primitives:

```txt
game service code
  -> generated RoomActors.Get/Local/Remote refs
  -> ActorDirectory cache / local actor invoker / remote actor invoker
  -> IActorRuntime / IClusterRouter
  -> ClusterActorEnvelope
  -> ClusterMessage / RouteLocation / transport adapter
```

`ActorDirectory` lives in `Lakona.Game.Server`. Business code should not
receive endpoint addresses or directory endpoint names.

The lower-level `ClusterMessage`, `ClusterActorEnvelope`, `IClusterRouter`,
`AskRemoteAsync`, `TellRemoteAsync`, and remote actor invoker APIs remain
important foundations. They are escape hatches, not the recommended daily
business API.

## Managed Lifecycle

Actor creation and destruction are local framework lifecycle operations exposed
through `IActorLifecycle.CreateLocalAsync` and `DestroyLocalAsync`. `AskAsync`,
`TellAsync`, generated actor refs, and scheduler ticks do not create actors.

Cross-node creation is a feature command to the owning feature; the owning
feature calls `CreateLocalAsync` on its own node.

Creation, placement, capacity, and idempotency belong at the feature-command
boundary. Once an actor exists, services and gateways should call it through
generated actor refs or `IActorRuntime`, not keep sending every actor method
through the feature command handler.

Missing actor behavior is deterministic:

- `AskAsync`, `TellAsync`, and generated actor refs return or throw structured
  `ActorNotFound` failures.
- scheduler ticks skip missing actors and report diagnostics.
- no normal actor call path implicitly creates the actor.

Destroy order is:

```txt
draining -> drain mailbox -> deactivate -> remove local actor -> unregister route
```

Failure to unregister a route is a routing/diagnostic problem; it does not
resurrect a destroyed actor or keep it callable in the local runtime.

## Actor Ticks

Actor ticks are framework-scheduled actor turns. They are declared by hotfix
feature descriptors and executed by the stable scheduler against the latest
loaded hotfix behavior table:

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);

        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}
```

The descriptor is a reloadable declaration, not a long-lived runtime loop
object. The framework owns timers, cancellation, mailbox entry, skipped-tick
diagnostics, slow-tick diagnostics, and shutdown.

Tick execution follows actor turn rules:

- one actor turn runs at a time for a given actor;
- at most one pending tick per tick source should exist;
- backlog policy must coalesce or skip instead of growing without bound;
- a thrown tick logs diagnostics and leaves actor state at the last completed
  turn;
- a failed hotfix reload keeps the previous tick behavior table active.

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
