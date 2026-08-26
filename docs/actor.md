# Actor Model

Lakona exposes one public actor API for game code:
`Lakona.Game.Server.Actors`. The same runtime owns actor identity, lifecycle,
dispatch, and diagnostics. Its internal mailbox implementation is a queueing
mechanism, not a second actor API or independently usable actor system.

Actors are the recommended way to model long-lived mutable game state such as
rooms, players, lobbies, matchmaking queues, leaderboards, and schedulers. An
actor is a concurrency boundary. It is not an ECS entity, an ORM model, or a
transparent distributed object.

The diagrams establish the ownership and execution model. The rules, API
shapes, and failure semantics following them remain the precise contract.

## Reading Map

| Question | Start here |
| --- | --- |
| Where do Actor state and game decisions live? | [Stable Actor, Hotfix Behavior](#stable-actor-hotfix-behavior) |
| Which generated selector should business code use? | [Generated Actor Access](#generated-actor-access) |
| How is an Actor identified? | [Actor Key Model](#actor-key-model) |
| What happens on a local or remote call? | [Runtime Layers](#runtime-layers) |
| How is an Actor created or destroyed safely? | [Managed Lifecycle](#managed-lifecycle) |
| How do startup replicas and timers work? | [Timers](#timers) |

## Responsibility Split

```mermaid
flowchart LR
    B["Game business code"] --> A["Generated ActorAccess<br/>business-facing facade"]
    A --> Q["Call routing<br/>existing activations only"]
    A --> P["Placement orchestration<br/>Create or Ensure"]
    P --> R["ActorActivationCatalog<br/>one authority keyed by ActorId"]
    Q --> R

    subgraph Cell["One runtime cell"]
        S["Stable Actor instance<br/>identity and mutable state"]
        M["Internal mailbox<br/>bounded sequential dispatch"]
        D["Hotfix behavior dispatch<br/>game decisions"]
        M --> D
        D --> S
    end

    R --> Cell
    C["Activation directory and<br/>cluster routing"] -.-> Q
    C -.-> P
    M -.->|"queue, timeout, drain,<br/>metrics and diagnostics"| X["Mailbox mechanism<br/>no independent Actor identity"]
```

`ActorActivationCatalog` keeps one entry keyed by the public `ActorId`. Each
entry owns the exact `ActorActivationId`, lifecycle state, actor instance,
Directory recovery claim, and mailbox. Queued `ActorWorkItem` values are
invoked directly by that entry. The mailbox is an execution queue, not another
activation registry.

## Stable Actor, Hotfix Behavior

Hotfix is mandatory for Lakona game servers. User-authored actor classes in
`Server.App` are stable state holders. Game decisions belong in matching
`Server.Hotfix` behavior classes.

```mermaid
flowchart LR
    subgraph App["Server.App — stable assembly"]
        I["Actor key and Actor type"]
        S["Long-lived mutable fields"]
        I --> S
    end

    subgraph Hotfix["Server.Hotfix — replaceable assembly"]
        B["Behavior methods<br/>game decisions"]
    end

    G["Generated dispatch snapshot"] --> B
    M["One accepted mailbox turn"] --> G
    B -->|"receives Actor as self"| S
    B --> R["Reply or accepted completion"]

    T["Long-lived timers, threads,<br/>events, or callbacks"] -.->|"must not be owned by Hotfix behavior"| B
```

```csharp
// Server.App
public readonly record struct RoomId(string Value);

[NodeRole("battle")]
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

```mermaid
flowchart TD
    I{"What is the caller trying to do?"}
    I -->|"Call an existing logical Actor"| R["Route(id)<br/>normal business path"]
    I -->|"Call after current-node ownership<br/>was already proven"| L["Local(id)<br/>process-local only"]
    I -->|"Create, ensure, or destroy activation"| P["Place(id)<br/>cluster-aware lifecycle"]
    I -->|"Call a registered startup group"| S["Startup(key)<br/>replica affinity"]

    R --> D["Resolve activation owner<br/>then dispatch"]
    L --> LR["IActorRuntime<br/>no route lookup"]
    P --> PS["IActorPlacementService<br/>CreateAsync, EnsureAsync, or DestroyAsync"]
    S --> SS["Registered startup lifecycle<br/>and replica selector"]

    N["Ordinary Route, Local, calls,<br/>and timers never create a missing Actor"] -.-> R
```

The generated overloads bind each business key type to `Actor<TKey>`, so an
actor/key mismatch is a compile error. The returned selectors are readonly
value types. The selector hot path caches actor-name and key-format metadata,
does not use `dynamic`, per-call reflection, or boxing, and allocates only the
canonical Actor identity strings it returns. The single root also holds shared
routing dependencies once instead of repeating them in every per-actor
collection instance.

Selector semantics:

- `Route(id)` is the normal business path. It owns actor-directory lookup and
  node selection before dispatch.
- `Local(id)` invokes only the process-local actor runtime and should be used
  only after the caller has already proven current-node ownership.
- `Place(id)` is the cluster-aware activation-lifecycle path. `CreateAsync`
  fails if the logical actor already has an activation; `EnsureAsync` returns
  the existing activation or creates one when absent; `DestroyAsync` retires
  the exact activation found by the operation and is idempotent when absent.
  Placement never moves an existing activation.
- `Startup(key)` routes through the lifecycle of an Actor group registered by
  `[HotfixConfigureActors]`.

`ActorAccess` is the only business-facing Actor facade. It expresses logical
Actor call and provisioning intent but owns no lifecycle state machine. Its
generated, business-key-specific `Place` overloads return the stable
framework-owned `ActorPlacement<TActor, TKey>` selector, which delegates
cluster orchestration to `IActorPlacementService`; the selected process always
asks the selected process's internal `ActorActivationCatalog` to perform the
physical activation. Generated access exposes logical cluster destruction, but
it does not expose current-node activation, directory mutation, or hidden
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

Create and Ensure lifecycle requests additionally carry the Hotfix build tag
that minted the capability. The owner compares it with the active Hotfix
snapshot and rejects an obsolete generation before materialization. Destroy
remains valid across a Hotfix reload when its exact activation proof still
matches, so reloading behavior cannot strand an activation that must be
released.

Direct `AddLakonaGameServerActors()` usage remains process-local and installs
neither cluster membership nor a cluster endpoint. Generated non-local
references require `AddLakonaGameServer`, whose endpoint is always backed by
committed membership.

Process-local actor-only hosts install no directory. `Local` and local
placement operate directly on the process runtime; `Route` requires clustered
composition and fails loudly when Actor Directory is absent.

The process-local Actors module owns the narrow `IActorDirectory` port used by
the activation catalog, placement, and invocation. Its acquire and release operations always
use an exact `NodeReference` and `ActorActivationId`; there is no node-only
registration fallback. Clustered composition supplies the distributed Actor
Directory adapter; range layout, transfer, recovery, and RPC binding remain
internal to the cluster-owned module. Startup affinity follows the same seam:
Actors owns the selection port while Cluster owns the distributed
shard/recovery adapter.
Lifecycle materialization remains an `ActorActivationCatalog` operation reached
through a cluster-owned RPC adapter.

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

`ActorContext.Id` is that complete, type-qualified runtime identity.
`ActorContext.Key` is the decoded business-key portion. Actor behavior that
needs the room, user, queue, or zone key uses `Key`; it must not treat the
complete `Id` as a business value or strip the actor-name prefix itself.

Long-lived protocols can pin the Actor wire name with `[ActorName]` and a
behavior method's wire name with `[ActorMethod("stable-name")]`. Generated
method keys and ids use the explicit method wire name, so the C# method can be
renamed without changing that part of the protocol identity. Actor, request,
and result type identities remain part of the method id.

Public behavior methods are remotely callable by default. Mark public
composition helpers with `[ActorIgnore]` to exclude them before method-shape
validation, code generation, and runtime dispatch. `[ActorMethod]` and
`[ActorIgnore]` are mutually exclusive, and an explicit method wire name must
not be empty.

Actor ids are global, stable business ids. They must not encode node id,
transport endpoint, callback state, connection id, or RPC session objects.
Good ids are shaped around the business object:

```txt
user/player-123
matchmaking/default
room/room-456
leaderboard/current
```

`ActorActivationCatalog` owns route registration for actors it creates. User code should
not separately publish an actor route for a local actor created through
framework lifecycle APIs.

Directory cache entries retain the exact owner incarnation and activation id.
A cache hit is usable only while that exact incarnation remains Active in the
current Membership view; otherwise it is evicted before routing. When a remote
host reports a stale route and explicitly proves that user code did not run,
the invoker removes the cached record, resolves Directory again, and retries at
most once. Indeterminate failures are never retried automatically, avoiding a
duplicate game operation when the first execution outcome is unknown.

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

Connection closure and other transport-level failures are translated at the
Actor boundary into typed `NodeUnavailable` results. Application code does not
need to catch exceptions belonging to the underlying RPC transport.

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

```mermaid
flowchart LR
    B["Game service code"] --> A{"Generated selector"}
    A -->|"Local(id)<br/>ownership already proven"| L["IActorRuntime"]
    A -->|"Route(id)"| AD["Activation cache and directory"]
    AD --> O{"Exact activation owner"}

    O -->|"current process"| L["IActorRuntime"]
    L --> LM["Local Actor mailbox"]
    LM --> LH["Hotfix behavior dispatch"]

    O -->|"Ready remote node"| RI["RemoteActorInvoker"]
    RI --> T["RpcClusterActorTransport"]
    T --> F["Dedicated raw ActorAsk or ActorTell<br/>fixed MemoryPack header + typed body"]
    F --> RS["Remote RpcSession"]
    RS --> CH["HotfixActorClusterHandler"]
    CH --> RM["Remote Actor mailbox"]
    RM --> RH["Hotfix behavior dispatch"]

```

Generated business behavior calls resolve ownership through the Actor
activation directory, then send directly to the exact Active owner over the
private cluster RPC connection. There is no parallel generic message or route
directory stack.

Membership and Actor ownership have separate responsibilities. The shared
Membership Table publishes which exact Active nodes advertise the required
concrete Actor-host descriptor; it does not decide or log the concrete owner of every
Actor. The placement selector uses that committed candidate set only when an
activation is missing. The Actor Directory partition owner then conditionally
publishes the sticky exact activation. The complete coordination
boundary belongs to
[Consensus Model And Scope](./cluster.md#consensus-model-and-scope).

Every Active Lakona server node contributes virtual Actor Directory partitions.
Each hash range has one exact partition owner. Consecutive Membership views
transfer moved records directly; skipped views reconstruct affected ranges from
the `ActorActivationCatalog` snapshots of surviving Active nodes as defined by
[Actor Directory DHT](./cluster.md#actor-directory-dht).

There is no additional actor-directory endpoint or provider configuration.
Ownership records remain in memory; complete cluster loss discards them.
Actor fields and mailbox contents are not replicated by either membership
consensus or the activation directory.

The Actor Directory port lives in `Lakona.Game.Server.Actors`; its distributed
adapter lives in the cluster-owned module. Business code should not receive
endpoint addresses or directory endpoint names.

`IActorRuntime` is a generated-support and advanced local runtime API. It
remains public because generated actor refs, hotfix service boundaries, tests,
diagnostics, and framework integrations may live in user assemblies, but it is
process-local and not the recommended daily business API. Ordinary gameplay
code should use generated actor selectors so local and routed placement intent
remains visible at the call site.

Framework code which already holds a canonical `ActorId` may use generated
`LocalExact<TActor>(actorId)` to avoid interpreting that identity as a business
key a second time. It is current-process-only and performs neither location
lookup nor creation; gameplay code normally keeps using typed business keys.

## Managed Lifecycle

Actor lifecycle has one business facade, one cluster orchestration seam, and
one local transaction owner:

- generated `ActorAccess.Place<TActor>(id)` is the business-facing lifecycle
  facade;
- `IActorPlacementService` resolves existing activations, discovers candidate
  hosts, applies rendezvous or a custom placement strategy, and sends one exact
  activation proposal to the selected process;
- internal `ActorActivationCatalog` is the only current-node activation owner.
  It acquires or releases Directory ownership and moves the local activation
  through `Creating`, `Activating`, `Valid`, `Deactivating`, and `Invalid`.

```mermaid
flowchart TD
    B["ActorAccess.Place(id)"] --> O{"Operation"}
    O -->|"CreateAsync"| C["Require activation to be absent"]
    O -->|"EnsureAsync"| E["Return existing exact activation<br/>when present"]
    O -->|"DestroyAsync"| X["Resolve and fence the current<br/>exact activation"]
    C --> R["Resolve authoritative directory state"]
    E --> R
    X --> R
    R -->|"missing"| H["Select from committed Ready<br/>Actor-host candidates"]
    R -->|"existing and Ensure"| ER["Return existing activation"]
    R -->|"existing and Create"| CF["ActorPlacementException"]
    H --> RP["Dispatch exact activation proposal<br/>to selected process"]
    RP --> TX["ActorActivationCatalog transaction"]

    subgraph Local["Selected process"]
        TX --> CE["Reserve Creating Catalog entry<br/>business admission closed"]
        CE --> DR["Acquire exact directory activation"]
        DR --> LR["Activate local cell and run start hook"]
        LR --> OK["Cache and return exact activation"]
    end

    TX -.->|"on failure, the selected Catalog releases<br/>or retains the exact fenced claim"| F["Typed placement or activation failure"]
```

Framework startup, remote Host RPC, placement, and hotfix rollback all converge
on `ActorActivationCatalog`; business code does not inject it or mutate directory/cache
state separately. `Route`, `Local`, ordinary Actor calls, and timer callbacks
never create missing actors.

The remote Host seam is assembly-internal framework orchestration, not an
application adapter interface. Create/Ensure and Destroy use distinct typed
commands so an RPC method cannot select the opposite lifecycle operation.
Each command carries one exact `ActorId + NodeReference + ActorActivationId`
target value; nullable GUID strings and runtime parsing are not part of the
interface.

Each Hotfix runtime snapshot owns a lifecycle dispatch catalog for its Actor
types. Per-request dispatch performs an ordinal Actor-name lookup and calls the
non-generic Catalog lifecycle entry point without reflection. The dispatch
catalog retires with that snapshot, so it cannot keep collectible Hotfix types
alive across reload or unload.

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
- `ActorAccess.Place<TActor>(id).DestroyAsync()` is idempotent when absent. It
  captures the current exact owner and activation id, then asks only
  that activation to retire. A delayed request cannot destroy a replacement.

An Actor that owns the decision that its business lifetime has ended calls
`Context.RequestDeactivation()`. This does not synchronously destroy the Actor
from inside its own mailbox. The runtime accepts the request only during an
active turn, discards it if that turn fails, and closes admission after a
successful reply before scheduling the same `ActorActivationCatalog` destruction
transaction. Coordinators use `Place(id).DestroyAsync()` for rollback and
external lifecycle decisions; normal self-completion does not require a
manager Actor.

Stop-hook and Actor deactivation exceptions are logged and cleanup continues;
an exception cannot leave a permanently draining activation. If accepted work
cannot drain before the deadline, or exact Directory release cannot be
confirmed, admission stays closed and the exact claim remains recoverable for
an explicit `Place(id).DestroyAsync()` retry. Lifecycle state never moves
backwards and a retired Actor is never reopened.

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
  publish or clear current-node routes as part of the Catalog transaction.

Activation failures are typed exceptions derived from `ActorHostingException`.
Important cases include `ActorAlreadyHostedException`,
`ActorHostingTypeMismatchException`, `ActorHostedElsewhereException`,
`ActorDirectoryUnavailableException`, and `ActorHostingStopException`.
They are internal activation details reached by framework lifecycle paths;
business placement failures are surfaced as `ActorPlacementException`.
Actor call exceptions remain separate; they describe failed calls to already
selected actors, not actor lifecycle operations.

Failed Create compensation has a framework-owned 30-second lifetime that is
independent of caller cancellation. It bounds both directory resolution and
exact activation release, including an adapter that does not cooperate with
cancellation. If that deadline expires, the operation reports a typed hosting
or placement failure whose message marks compensation as unconfirmed; it does
not report the activation as released. The original activation failure remains
in the exception cause chain.

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

```mermaid
sequenceDiagram
    participant C as lifecycle caller
    participant P as placement and exact Host RPC
    participant H as ActorActivationCatalog
    participant D as Actor Directory and cache
    participant M as runtime cell and mailbox
    participant A as stable Actor instance

    C->>P: Place(id).DestroyAsync
    P->>D: Resolve exact owner and activation id
    P->>H: Destroy only that exact activation
    H->>D: Resolve current route ownership
    alt Another node owns the route
        H->>D: Leave remote route intact
        H->>M: Remove only stale matching local state
        H-->>C: Current-node cleanup complete
    else Route is local or absent
        H->>M: Close admission
        Note over M: Racing calls are rejected and cannot queue behind deactivation
        H->>M: Drain already accepted work
        M->>A: Run deactivation
        H->>D: Conditionally release exact activation
        H->>M: Remove exact cell from registry
        alt Drain times out or release is unconfirmed
            H->>D: Keep exact activation reserved
            H-->>C: Typed lifecycle failure
        else Destroy succeeds or Actor was already absent
            H-->>C: Actor absent locally and route cleared
        end
    end
```

Closing mailbox admission first stops new work while keeping the exact activation
reserved until all accepted work and the stop hook have finished. Only then does
`ActorActivationCatalog` conditionally unregisters it. Stop-hook exceptions are
logged while cleanup continues. If draining cannot finish, the route remains
reserved and no replacement can overlap. If another node owns the route,
`DestroyAsync` leaves that route intact and only removes stale current-node
cache/local actor state for the requested type.

Recovery reads the same Catalog entries used for local dispatch; there is no
second activation registry. A proposed claim enters the Catalog as `Creating`
before Directory acquisition begins, remains unavailable to business calls,
and is revalidated before mailbox admission opens. Destroy and failed-create
rollback keep the Catalog entry and its recovery claim until exact release succeeds. The failed-Create
cleanup deadline bounds how long the foreground placement, Hotfix rollback, or
shutdown path waits; expiry leaves the fenced `Deactivating` entry available to
recovery and reports an unconfirmed compensation outcome.

Local stop closes mailbox admission before deactivation is queued. Calls that
race with stop are rejected through the normal rejection and dead-letter
diagnostic path; they cannot queue behind deactivation and reactivate the actor.
If the caller's drain timeout expires, the cell remains `Draining` until its
already accepted work finishes, then the runtime removes that exact cell from
the registry so the public `ActorId` can be created again.

Graceful node shutdown first puts the `ActorActivationCatalog` into drain mode.
It rejects new activations, retires every current activation through the normal
deactivation path, and releases each exact Directory route while Directory and
cluster transport are still running. Runtime disposal remains the final safety
net for abrupt or partially constructed shutdown: it closes any mailbox still
present without rerunning actor deactivation hooks, waits for completion, and
rejects later lifecycle, dispatch, state, metrics, or diagnostics operations
with `ObjectDisposedException`. Actor construction racing drain or disposal
cannot publish a new registry cell.

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

An affinity generation left `Pending` by an indeterminate retain response keeps
retrying the same exact target while that target remains Ready. Once Membership
has committed that exact target out, execution there is no longer possible and
the affinity owner may advance the same key to a higher generation on a new
compatible target even when the affinity shard owner itself did not change.

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

```mermaid
flowchart LR
    O["ActorRuntimeOptions"] --> R["ActorActivationCatalog"]
    R --> C["One runtime cell per local ActorId"]
    C --> M["ActorMailbox"]
    M --> MC["MailboxCapacity"]
    M --> ST["SlowMessageThreshold"]

    R --> T["Call timeout and response completion"]
    R --> L["Deactivation timeout and drain deadline"]
    R --> D["Dead letters, events, metrics,<br/>traces, and slow-message diagnostics"]
```

The runtime also owns call timeout, diagnostic events, dead letters, slow
message reporting, and call-timeout handling. These signals therefore carry
the public actor identity directly and need no mapping layer.
