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

All actors are framework-managed in the first version. Do not introduce a
`UserManaged` or `ActorLifetime` split until a concrete repeated need exists.

Generated lifecycle operations are local-only:

```csharp
await rooms.SpawnAsync(roomId, request, cancellationToken);
await rooms.DestroyAsync(roomId, cancellationToken);
```

Spawn claims placement in `ActorDirectory`, creates the actor locally, and
invokes the spawn hook if present. Hook or local creation failure unregisters
placement and rolls back the local actor.

Destroy unregisters placement first, then invokes the destroy hook if present
and removes the local actor. Hook or stop failure attempts to re-register
placement for the still-local actor.

Lakona does not provide `SpawnRemoteAsync` or `DestroyRemoteAsync`. Cross-node
creation or destruction should be explicit business commands to a manager actor
or service on the target node.

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
