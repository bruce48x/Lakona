# Actor Model

Lakona exposes one public actor API for game code:
`Lakona.Game.Server.Actors`. The same runtime owns actor identity, lifecycle,
dispatch, and diagnostics. Its internal mailbox implementation is a queueing
mechanism, not a second actor API or independently usable actor system.

Actors are the recommended way to model long-lived mutable game state such as
rooms, players, lobbies, matchmaking queues, leaderboards, and schedulers. An
actor is a concurrency boundary. It is not an ECS entity, an ORM model, or a
transparent distributed object.

## Responsibility Split

```txt
Lakona.Game.Server.Actors             internal Actor mailbox
--------------------------------     --------------------------------
Game actor identity                  Bounded queue and backpressure
Actor base class and context         Sequential work-item dispatch
IActorRuntime                        Call/response completion
DI activation and lifecycle          Queue/response timeout tracking
Single local actor registry          Stop and drain state
Cluster routing                      Metrics, traces, and diagnostics
Hotfix behavior dispatch             No independent actor identity or lifecycle
```

`LakonaActorRuntime` keeps one registry keyed by the public `ActorId`. A runtime
cell owns the actor instance and one mailbox; queued `ActorWorkItem` values are
invoked directly by that cell. There is no numeric kernel actor id, actor ref,
adapter actor, or envelope-to-envelope conversion between the public runtime
and mailbox.

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
public sealed partial class RoomBehavior
{
    public ValueTask<JoinRoomReply> JoinAsync(
        RoomActor self,
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

Generated actor APIs expose one injectable access root for local, routed,
placement, and startup calls. Business code does not import one collection
class per actor or hand-write actor ids, route keys, serializers, or
reply-correlation plumbing.

```csharp
public sealed class ActorAccess
{
    public LocalActor<TActor> Local<TActor>(RoomId id)
        where TActor : Actor<RoomId>;

    public ActorRoute<TActor> Route<TActor>(RoomId id)
        where TActor : Actor<RoomId>;

    public ActorPlacement<TActor, RoomId> Place<TActor>(RoomId id)
        where TActor : Actor<RoomId>;

    public StartupActor<TActor, string> Startup<TActor>(string key)
        where TActor : Actor;
}
```

Inside the same actor turn, call the actor instance directly. Across actor
boundaries, use the generated access root:

```csharp
await actors.Route<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, cancellationToken);
await actors.Local<RoomActor>(roomId).PostAsync(static behavior => behavior.RunTickAsync, request, cancellationToken);
```

The generated overloads bind each business key type to `Actor<TKey>`, so an
actor/key mismatch is a compile error. The returned selectors are readonly
value types. Selecting an actor does not use `dynamic`, reflection-based
construction, boxing, or a per-call heap allocation. The single root also
holds shared routing dependencies once instead of repeating them in every
per-actor collection instance.

Selector semantics:

- `Route(id)` is the normal business path. It owns actor-directory lookup and
  node selection before dispatch.
- `Local(id)` invokes only the process-local actor runtime and should be used
  only after the caller has already proven current-node ownership.
- `Place(id)` is the cluster-aware activation-provisioning path. `CreateAsync`
  fails if the logical actor already has an activation; `EnsureAsync` returns
  the existing activation or creates one when absent. Placement never moves an
  existing activation.
- `Startup(key)` routes through the lifecycle of an Actor group registered by
  `[HotfixConfigureActors]`.

`ActorAccess` is the only business-facing Actor façade. It expresses logical
Actor call and provisioning intent but owns no lifecycle state machine.
Generated placement selectors delegate cluster orchestration to
`IActorPlacementService`; the selected process always performs physical
activation work through the internal `ActorHosting` module. Generated access
must not expose current-node destruction, directory mutation, or hidden
call-triggered creation.

Generated actor selectors expose generic `CallAsync` and `PostAsync` helpers.
`CallAsync` is completion-aware and surfaces the behavior reply or a typed
actor call failure. `PostAsync` is acceptance-only and completes once the
mailbox or remote transport accepts the work. Lower-level status-returning
APIs remain available for framework internals and boundary services.

Remote Actor request and reply payloads use the fixed MemoryPack serializer
owned by the `Lakona.Game.Server` cluster channel. They do not use the
client-facing endpoint serializer.

Actor API DTOs are protocol contracts rather than Actor state. They must live
in stable, non-hotfix assemblies and use
`[MemoryPackable(GenerateType.VersionTolerant)]` with explicit,
never-reassigned `MemoryPackOrder` values. This permits additive rolling
changes while keeping hotfix generations out of the serialized type graph.

Generated remote calls keep the request as its compile-time DTO type until the
cluster client writes it. A typed MemoryPack codec is closed once when the
Hotfix dispatch snapshot is published; invocation performs no
`Type`-based serialization, `MakeGenericMethod`, or reflective serializer
dispatch. The request header and DTO body are written directly into the final
RPC request frame. The receiver decodes a slice of that frame, dispatches the
cached typed method codec, and writes the reply header and result directly into
the final RPC response frame.

The Actor request/reply RPC methods are dedicated raw methods on the private
cluster service. They do not pass through `ClusterActorEnvelope`,
`ClusterMessage`, or the general cluster message DTO. This boundary has one
explicit owner at each stage: the RPC client owns the outbound frame until
send completes, the RPC session owns the inbound frame while dispatch runs,
and the caller owns and disposes the returned response frame after the typed
reply has been materialized. Copied `byte[]` payload wrappers and a replaceable
Actor serializer seam are forbidden.

Every routed request also carries the exact cluster, node, membership, Actor
activation, and deadline proof required before mailbox dispatch. Their
different lifetimes, validation rules, and retry boundary are defined in
[Distributed Identity And Request Lifetime](cluster.md#distributed-identity-and-request-lifetime).

Direct `AddLakonaGameServerActors()` usage remains process-local. Generated
non-local references require the normal cluster endpoint services because the
wire codec and raw Actor transport are framework-owned rather than
application-replaceable.

Process-local actor-only hosts use `InMemoryActorDirectory` by default. They do
not need cluster or actor-directory configuration unless they opt into routed
cross-node actor access.

## Actor Key Model

Actor key type is declared in the actor base type:

```csharp
public sealed class RoomActor : Actor<RoomId>
{
}
```

This avoids separate key attributes and avoids generator guessing. The
generator uses `TKey` to generate constrained
`Local<TActor>(TKey id)` and `Route<TActor>(TKey id)` overloads.

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
    var reply = await actors
        .Route<RoomActor>(roomId)
        .CallAsync(static behavior => behavior.JoinAsync, request, cancellationToken);
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

Mailbox queue totals are maintained at enqueue, dequeue, rejection, and drain
boundaries. Metrics collection reads the aggregate counter in constant time;
it must not enumerate every live Actor mailbox during a scrape.

Detail endpoints are disabled by default. Any endpoint that exposes more than
aggregate actor diagnostics requires explicit diagnostics detail mode.

## Runtime Layers

The generated typed API has separate local and remote execution branches:

```txt
game service code
  -> generated ActorAccess.Local<TActor>/Route<TActor> selectors
     -> local owner: IActorRuntime -> Actor mailbox -> Hotfix dispatch
     -> remote owner:
        Actor activation cache/directory
        -> RemoteActorInvoker
        -> RpcClusterActorTransport
        -> dedicated raw ActorAsk/ActorTell RPC
        -> fixed MemoryPack header + typed body in the final TCP RPC frame
        -> remote RpcSession / HotfixActorClusterHandler
        -> Actor mailbox -> Hotfix dispatch
```

Generated business behavior calls resolve ownership through the Actor
activation directory, then send directly to the exact Ready owner over the
private cluster RPC connection. They do not use `IClusterRouter`,
`IRouteDirectory`, `ClusterActorEnvelope`, or the general `ClusterMessage`
payload and reply path.

Membership consensus and Actor ownership have separate responsibilities.
Membership consensus publishes which exact Ready nodes advertise the required
`ActorHosts` capability; it does not decide or log the concrete owner of every
Actor. The placement selector uses that committed candidate set only when an
activation is missing. The activation directory then commits the sticky owner
through its independent partition-majority protocol. The complete coordination
boundary belongs to
[Consensus Model And Scope](./cluster.md#consensus-model-and-scope).

Every full Lakona server node hosts the replicated activation-directory module
and can store directory replicas. This does not mean every node has a complete
copy. Each activation is stored on the selected partition replicas and its
exact owner; authoritative cold lifecycle reads reconcile current Ready nodes
as defined by
[Activation Directory](./cluster.md#activation-directory). Peers are formation
and discovery hints only: they do not own the directory or receive every
resolve, acquire, or release operation.

There is no additional actor-directory endpoint or provider configuration.
Ownership records remain in memory and are replicated for availability within
the current cluster incarnation; complete cluster loss still discards them.
Actor fields and mailbox contents are not replicated by either membership
consensus or the activation directory.

`ActorDirectory` lives in `Lakona.Game.Server`. Business code should not
receive endpoint addresses or directory endpoint names.

The lower-level `ClusterMessage`, `ClusterActorEnvelope`, `IClusterRouter`,
`AskRemoteAsync`, and `TellRemoteAsync` APIs remain available for framework
control traffic, advanced integrations, and tests. On that
lower-level `RemoteActorGateway` path,
`ClusterActorRouteKeys.ForReply(nodeId)` is a destination-local reply handler
key and is never a cluster directory registration. None of these primitives
describe the generated Hotfix Actor behavior-call path above; they are escape
hatches, not the recommended daily business API.

`IActorRuntime` is a generated-support and advanced local runtime API. It
remains public because generated actor refs, hotfix service boundaries, tests,
diagnostics, and framework integrations may live in user assemblies, but it is
process-local and not the recommended daily business API. Ordinary gameplay
code should use generated actor selectors so local and routed placement intent
remains visible at the call site.

## Managed Lifecycle

Actor lifecycle has one business façade, one cluster orchestration seam, and
one local transaction owner:

- generated `ActorAccess.Place<TActor>(id)` is the business-facing creation
  façade;
- `IActorPlacementService` resolves existing activations, discovers candidate
  hosts, applies rendezvous or a custom placement strategy, acquires activation
  ownership, and dispatches to the selected process;
- internal `ActorHosting` is the only current-node physical activation owner.
  It runs `CreateAsync`, `EnsureAsync`, or `DestroyAsync` while keeping the
  local runtime, `ActorDirectory`, and `ActorDirectoryCache` consistent.

Framework startup, remote Host RPC, placement, and hotfix rollback all converge
on `ActorHosting`; business code does not inject it or mutate directory/cache
state separately. `Route`, `Local`, ordinary Actor calls, and timer callbacks
never create missing actors.

Creation, placement, capacity, and idempotency belong at the actor placement
boundary. Once an actor exists, services and gateways should call ordinary
business behavior through generated actor refs. Raw `IActorRuntime.AskAsync`
and `TellAsync` remain framework-level escape hatches.

Business placement semantics:

- `ActorAccess.Place<TActor>(id).CreateAsync()` is strict across the cluster.
  It fails with `ActorPlacementException` if the directory already contains an
  activation, another concurrent caller wins activation ownership, or the
  selected host reports an existing owner.
- `ActorAccess.Place<TActor>(id).EnsureAsync()` is idempotent. It returns the
  existing activation or creates one when absent.

Current-node hosting semantics:

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
They are internal hosting details reached by framework lifecycle paths;
business placement failures are surfaced as `ActorPlacementException`.
Actor call exceptions remain separate; they describe failed calls to already
selected actors, not actor lifecycle operations.

In a multi-node cluster, transport failure, serialization or deserialization
failure, and an unavailable or invalid directory reply are all surfaced as
`ActorDirectoryUnavailableException`. Caller-requested cancellation remains an
`OperationCanceledException` and is not wrapped as directory unavailability.

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

Local stop closes mailbox admission before deactivation is queued. Calls that
race with stop are rejected through the normal rejection and dead-letter
diagnostic path; they cannot queue behind deactivation and reactivate the actor.
If the caller's drain timeout expires, the cell remains `Draining` until its
already accepted work finishes, then the runtime removes that exact cell from
the registry so the public `ActorId` can be created again.

Runtime disposal is terminal. It closes every current mailbox without running
actor deactivation hooks, waits for their completion, and rejects later
lifecycle, dispatch, state, metrics, or diagnostics operations with
`ObjectDisposedException`. Actor construction racing disposal cannot publish a
new registry cell after disposal begins.

## Timers

Hotfix timers are framework-owned callbacks created through `LakonaTimer`.
Startup service groups are declared from the Hotfix assembly's single optional
`[HotfixStartup]` composition root. Large Actor registration sets should be
split into explicit helper or extension-method calls from that root; multiple
startup roots reject the candidate before any registration executes. Periodic
work should stay inside hotfix actor behavior or explicit timer callbacks:

```csharp
[HotfixStartup]
public static class GameHotfixStartup
{
    [HotfixConfigureActors]
    public static void Actors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>();
    }
}

public sealed record BattleRuntimeTick(string QueueId);

[HotfixTimer]
public sealed partial class BattleRuntimeTimers
{
    public ValueTask TickAsync(TimerTick<BattleRuntimeTick> tick)
    {
        // Enter generated actor selectors or services here.
        return default;
    }
}
```

Each capable node starts one physical replica. Generated business code calls
`.Startup(key)`; the key only supplies affinity to the fixed selector and is not
an actor id. The parameterless registration uses rendezvous hashing by default;
pass a selector to `RegisterStartup<TActor, TKey>(selector)` when the product
requires another affinity algorithm. The framework advertises a replica after
`[ActorStart]` succeeds and withdraws it before removal. Same-key failover is
limited to attempts known not to have executed. State is local to each replica,
so failover does not preserve an in-memory queue. Use the exact physical actor
id only for internal lifecycle work such as a replica-owned timer.

The method name is explicit on purpose. Use `nameof(...)` so the call site shows
which callback will run and normal refactoring tools keep the declaration in
sync. The scheduler stores the method name rather than a delegate because a
delegate could keep an old reloadable hotfix assembly generation alive after
reload.

One process-wide scheduler owns all Hotfix timer registrations across reloadable
generations. Its active population is bounded by
`Lakona:Timers:MaxActiveTimers`; capacity exhaustion rejects creation instead of
silently dropping or replacing a business timer. Destroy remains constant-time
on the ordinary path, while accumulated stale priority-queue entries trigger an
amortized rebuild. Timer population and rejection diagnostics use the
low-cardinality `Lakona.Game.Timer` meter.

## Analyzer Boundary

Analyzer rules apply at the public actor and hotfix boundary:

| Rule | Scope |
| --- | --- |
| `LKNHOTFIX011` no actor business methods in stable app | hotfix authoring |

Actor isolation and thread-safety rules target the public game-facing facade,
not the internal mailbox implementation.

## Configuration Flow

```txt
ActorRuntimeOptions
  -> LakonaActorRuntime
     -> ActorMailbox per runtime cell
        -> MailboxCapacity
        -> SlowMessageThreshold
```

The runtime also owns call timeout, diagnostic events, dead letters, slow
message reporting, and call-timeout handling. These signals therefore carry
the public actor identity directly and need no mapping layer.
