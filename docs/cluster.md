# Cluster

Lakona cluster support is process-local game state coordinated by an ephemeral,
replicated control plane. Every joined node stores membership state; seed
endpoints are only discovery contacts. Framework state is intentionally not
persisted to Postgres.

## Terms

| Term | Meaning |
| --- | --- |
| `NodeId` | Stable operator-facing process name, such as `data-1`. It is not a fencing token. |
| `ClusterIncarnationId` | Identity of one complete in-memory cluster lifetime. A deliberate complete restart creates a new value. |
| `NodeIncarnationId` | Identity of one process lifetime. Restarting the same `NodeId` creates a new value. |
| `MembershipViewId` | Monotonic identity of a committed membership or descriptor change. |
| `NodeReference` | Exact `(cluster, node, node incarnation)` identity used for authoritative dispatch. |
| Seed | Unordered endpoint used to contact an existing cluster during join. It is not a leader or directory owner. |
| Actor activation | Sticky `(actor, owner reference, activation id, version)` ownership record. |

“Seed” is best translated as “引导节点” or “发现入口” in this design. Avoid
translating it as “主节点”: after join it has no special authority.

## Configuration And Bootstrap

Exactly one process creates a fresh cluster:

```json
{
  "Lakona": {
    "Node": { "Id": "data-1" },
    "ActorHosts": [ "user", "matchmaking", "leaderboard" ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "BootstrapNewCluster": true,
      "Seeds": []
    }
  }
}
```

Other processes join through one or more contacts:

```json
{
  "Lakona": {
    "Node": { "Id": "data-2" },
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Seeds": [
        "tcp://10.0.0.1:21001",
        "tcp://10.0.0.3:21003"
      ]
    }
  }
}
```

`BootstrapNewCluster=true` and non-empty `Seeds` are mutually exclusive. An
unreachable seed never authorizes implicit bootstrap because the old cluster
may still have a majority elsewhere. Seed order is irrelevant: contacts can
redirect a joiner to the elected leader.

Replicated hosting is enabled when either bootstrap or seeds are configured.
Legacy directory services remain available for compatibility when neither is
configured, but they are not on the replicated membership, actor, session, or
notification hot paths.

The one bootstrap setting authorizes a fresh cluster incarnation. Operators
must not start multiple independent bootstrap processes for the same logical
deployment. After complete cluster loss, intentionally starting a fresh
bootstrap accepts that all in-memory Actors, sessions, directory metadata, and
reliable-push state from the prior incarnation are gone.

## Cluster RPC Composition

Configuration describes addresses and topology; the application composition
root selects the cluster RPC implementation. A generated MemoryPack server is
explicit:

```csharp
using Lakona.Game.Cluster.Rpc.Serializer.MemoryPack;
using Lakona.Game.Cluster.Rpc.Transport.Tcp;

return await LakonaGameServer.RunAsync(args, static server => server
    .UseClusterRpc(
        TcpClusterRpcTransport.Default,
        MemoryPackClusterRpcSerializer.Default)
    // client-facing endpoint registrations follow
);
```

The packages are deliberately separated:

- `Lakona.Game.Cluster.Rpc` owns routing RPC, the channel, and extension
  contracts.
- `Lakona.Game.Cluster.Rpc.Transport.Tcp` owns both outbound TCP connections
  and the inbound TCP listener.
- `Lakona.Game.Cluster.Rpc.Serializer.Json` and
  `Lakona.Game.Cluster.Rpc.Serializer.MemoryPack` own serializer protocol IDs
  and serializer construction.

`ClusterRpcChannel` is the single internal authority for the chosen pair. It
validates endpoint schemes, creates pooled outgoing clients, creates the local
listener, and performs a small fixed-format protocol negotiation before the
RPC serializer sees a frame. Incompatible serializer protocol IDs are rejected
as connection-local failures. The negotiation adds one round trip only when a
cluster connection is established; steady messages reuse pooled clients.

Custom cluster transports implement `IClusterRpcTransport`, including both
connect and listen behavior, and custom serializers implement
`IClusterRpcSerializer` with a stable protocol ID. This keeps WebSocket, KCP,
TLS, or future transports outside the framework core while preventing inbound
and outbound halves from being configured inconsistently.

## Replicated Membership

Every joined node automatically participates in the same in-memory membership
state machine. There is no manually assigned directory-replica role and no
cluster Postgres requirement.

A joining node:

1. creates a fresh `NodeIncarnationId`;
2. contacts any configured seed and follows the current leader;
3. installs the committed snapshot and log tail as a non-voting learner;
4. is promoted through joint consensus after catch-up;
5. runs recovery while distributed admission remains closed;
6. commits its Ready descriptor and opens admission only after authority is
   proven.

Membership snapshots contain exact node references, lifecycle state, cluster
RPC endpoints, actor-host descriptors, Startup descriptors, labels, and opaque
metadata on those descriptors. High-cardinality Actor activations and sessions
do not enter the global membership log.

Every caught-up member is currently a voter. This deliberately targets small,
normally odd-sized clusters. Leader heartbeat, replication, election, and
majority work grow with member count. A bounded automatic voting committee is
deferred until measurements justify its complexity; operators do not manually
manage replica assignments in the current model.

The replicated log and snapshots are bounded and validated. Membership reads
use one atomically published local snapshot through `IClusterMembership`, so
steady discovery and exact endpoint lookup require no seed or leader round
trip.

Replication progress is tracked per exact voter. Append responses report the
receiver's actual membership view and log match index. If a voter misses a
commit or rejects a heartbeat because it is behind, the leader backs up to the
last matching position and sends bounded committed batches on later heartbeat
rounds. A voter counts toward a quorum proof only after both its log and its
published view have caught up. A transient commit-delivery failure therefore
cannot leave a running voter permanently stranded on an old view.

### Adding And Restarting Nodes

Adding a fourth node follows learner catch-up and joint-consensus promotion.
It does not move existing Actor ownership. The new node becomes eligible for
future placements and may receive repaired activation-directory replicas.

Restarting a process with the same stable `NodeId` creates a different exact
reference. The leader waits through the old incarnation's authority window,
joint-removes it, and then admits the replacement as a learner. An old process
cannot become authoritative again merely by reconnecting.

Cluster-node authentication and authorization are separate from this identity
and fencing design. Deployments must still isolate and protect the cluster
network.

## Heartbeat Failure, Fencing, Gate, And Barrier

The heartbeat/control loop is supervised. A transient exception cannot silently
terminate it: failures are observed, retried with bounded backoff, and reflected
in authority state. The safety decision is based on recent quorum proof rather
than on whether one asynchronous loop happened to be running.

### Epoch Fencing

Epoch fencing means every authoritative message carries enough generation
identity to prove which lifetime it belongs to. For node work this includes
cluster incarnation, node incarnation, and committed view. Actor work also
carries `ActorActivationId` and activation version.

A receiver rejects an old cluster, replaced node incarnation, or stale Actor
activation before business mailbox dispatch. Fencing prevents a delayed old
process or cached route from becoming a second owner. External databases that
require strict single-writer behavior must also store and compare the Actor
fencing token; the framework cannot fence writes after they leave the process.

### Distributed-Work Admission Gate

The gate is a process-wide valve in front of new distributed work. It remains
open only while the node has valid majority authority. Loss of authority closes
it and stops new Actor activation/delivery work; consensus, management, health,
and recovery traffic remain available.

This separates “the process is alive” from “the process is currently allowed
to act as an owner.” A minority partition therefore fails closed instead of
continuing to serve conflicting state.

### Recovery Barrier

Regaining cluster contact is not enough to reopen the gate. The recovery
barrier runs registered recovery participants in order while admission remains
closed. Membership and descriptors are validated and committed before the
coordinator marks the node active. Any participant failure leaves the gate
closed; it cannot expose a half-recovered runtime.

Together these mechanisms address the heartbeat-loop failure mode:

```text
supervised control loop
  -> valid quorum proof
  -> closed admission gate
  -> recovery barrier succeeds
  -> Ready descriptor committed
  -> gate opens
```

Loss of a majority intentionally stops control-plane changes and new
distributed admission. Retrying forever while continuing business work would
hide a split brain and is not considered recovery.

## Sticky Actor Placement

Rendezvous hashing is the default initial-placement policy, not live ownership.
Hashing alone would remap roughly one quarter of keys when a fourth equal node
joins a three-node cluster. That is unacceptable for strong in-memory game
Actors, so an activation record remains authoritative after first placement.

The flow is:

```text
resolve sticky activation
  -> live exact owner exists: dispatch directly
  -> no activation: run placement selector, then atomically acquire
  -> old exact owner removed from membership: acquire a higher generation
```

Adding a node moves zero existing Actors. New Actors may select it. A failed
owner can be superseded only after its exact incarnation has been committed out
of membership; the replacement receives a new activation id and higher
version. Because Actor state is in memory, the replacement starts without the
failed process's state unless the application provides its own persistence or
recovery.

### Activation Directory

`IActorActivationDirectory.AcquireAsync` is a framework API used by
`ActorPlacementService`; business users do not call it for normal Actor
creation. It atomically returns either the already committed sticky record or
the winning proposal. This adds a control-plane request only on activation and
placement, not on every Actor message. Warm invocations use the cached record
and send directly to the exact owner.

Activation keys map to 1024 protocol partitions. Each partition selects up to
three Ready members by canonical rendezvous hashing. Acquire and release require
a replica majority. Reads require an agreeing majority and repair missing or
stale copies. Every node is eligible automatically; there is no special seed,
directory node, or Postgres table.

After a healthy three-to-four-node expansion, every new three-replica set still
contains two old members, so quorum read repair can copy metadata without
changing Actor owners. Large/concurrent topology changes, throttled partition
handoff, and reconstruction after every replica loses a record are deferred;
the current small-cluster implementation fails closed when it cannot obtain an
agreeing majority.

The first node creating `useractor 110` does not broadcast that fact to every
node. It commits the activation to the selected partition replica majority.
Another node learns the owner when it first resolves or acquires that Actor,
then caches the exact record. This avoids cluster-wide high-cardinality
membership writes.

### Default And Custom Placement

Actor placement uses rendezvous hashing by default, so `RoomActor` needs no
placement declaration in `HotfixStartup`. Startup Actors still need a
registration to declare that their replicas exist; its parameterless overload
selects rendezvous affinity:

```csharp
actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>();
```

Ordinary Actor types are discovered from their Hotfix behavior and lifecycle
descriptors, independently of placement overrides. Omitting a placement entry
therefore does not remove the Actor from node host descriptors or remote
creation; `Lakona:ActorHosts` still chooses which nodes can host it.

Applications may preserve a product-specific algorithm:

```csharp
actors.RegisterPlacement<UserActor, UserId>(static context =>
    context.Candidates[(int)(StableHash(context.Key.Value)
        % (uint)context.Candidates.Count)]);
```

The selector runs only for a missing or safely supersedable activation and must
return one exact offered candidate. Candidate order, membership, or selector
code changes never rehash an existing live Actor. Agar intentionally keeps its
existing FNV-1a modulo selector for `UserActor` and its Startup Actors, while
`RoomActor` uses the framework rendezvous default by having no placement entry
in `HotfixStartup.cs`.

Placement registration is therefore an override, not a requirement. Startup
registration retains both parameterless and selector overloads because it also
declares the replica type. The implicit placement default and parameterless
Startup registration use the same canonical rendezvous score and node-id tie
break, so the two defaults cannot drift apart.

Startup Actor selection uses the same sticky idea for a business key. The
first selected replica is recorded under an internal Startup-affinity Actor id,
so adding a Startup replica affects only new keys. The selected replica Actor
also has its own activation record. The affinity record answers “which replica
owns this key”; the replica activation supplies the exact `NodeReference`,
activation id, and version checked before mailbox dispatch. Keeping these two
responsibilities separate preserves sticky affinity without weakening node or
Actor fencing.

Live Actor migration and automatic rebalance are not implemented. They require
an application-owned snapshot contract and mailbox barriers and remain a
separate future design.

## Session Ownership And Notification Routing

Framework-created session ids contain an opaque, versioned locator for the
exact gateway owner:

```text
version + cluster incarnation + gateway NodeId
        + gateway node incarnation + random local id
```

Business code still treats `SessionId` as opaque. During notification delivery,
the framework decodes the locator, validates it against the local membership
snapshot, and either dispatches locally or sends directly to that exact gateway.
No seed or route-directory network lookup is required. A malformed locator or
old incarnation is rejected rather than redirected to a process that reused the
same stable name.

`MatchmakingNotifier.Publish` illustrates the complete path:

1. generated synchronous notification code builds one command per target
   session;
2. the local router reserves per-session and process-wide queue capacity;
3. background delivery decodes each session's exact gateway locator;
4. local targets enter the local session/reliable-push runtime directly;
5. remote targets are grouped by exact gateway and sent as bounded batches;
6. the owner validates its exact incarnation and local session, assigns reliable
   sequence/outbox state when enabled, and invokes the connection callback.

`_routes.ResolveAsync` remains as a compatibility-shaped internal boundary, but
under replicated hosting `MembershipSessionRouteDirectory` decodes the session
locator and checks the local membership snapshot. It does not query the first
seed or perform a distributed directory lookup.

### Synchronous Admission Is Intentional

Generated notification methods remain synchronous for high-frequency Room
broadcasts. `Accepted` means the producer-local bounded queue owns the complete
command; it does not mean the remote gateway or client has accepted it.
Changing this default to owner-confirmed async delivery would add a route/network
wait to every push and is explicitly deferred pending measurement.

The queue never overwrites, coalesces, or deduplicates an older accepted
notification. Those are business semantics, not framework policy.

Remote batches are keyed by exact gateway reference. The default maximum wait
is 10 ms and can be set to zero. Count and byte limits flush a batch early; an
individual command that cannot fit the byte budget returns `Backpressure`.

```json
{
  "Lakona": {
    "Notifications": {
      "BatchWindowMilliseconds": 10,
      "MaximumBatchSize": 256,
      "MaximumBatchBytes": 262144,
      "MaximumPendingPerSession": 256,
      "MaximumPendingPerProcess": 65536
    }
  }
}
```

The router preserves FIFO per session with one active drain per session. A
fixed session-affine worker pool is deferred and recorded as a possible
large-session-count optimization. Process-local admitted queues and reliable
outboxes may be lost with their process; the accepted ephemeral model does not
turn them into durable delivery.

## Agar Gameplay Endpoints

The framework owns only its node-to-node cluster endpoint. Client and gameplay
transports remain business concerns. Agar matchmaking first places and creates
the Room Actor. `RoomBehavior.CreateAsync` runs on the exact owner, reads that
process's battle service endpoint, stores the full advertised transport, host,
port, and path in Room state, and returns it through
`RoomSettlementResult.Snapshot.RuntimeGateway`. Matchmaking then copies this
authoritative value into player assignments. Endpoint selection and Actor
placement therefore cannot choose different nodes.

The Room behavior selects endpoints by the application service labels `battle`
and `battle-runtime`, not by a framework transport type. Agar currently uses
KCP, but changing the gameplay transport does not require cluster or placement
changes. The advertised address must be used instead of the listener bind
address because wildcard binds, containers, NAT, and proxies may make them
different.

Agar no longer needs `LakonaClusterPostgres`. Its separate `AgarGamePostgres`
setting remains application persistence and is unrelated to cluster authority.
Existing node business roles and custom placement selectors remain intact.

## Performance Scope And Deferred Risks

| Area | Current decision | Recorded risk |
| --- | --- | --- |
| Actor scale-out | Existing Actors remain sticky; only new activations use new capacity. | A hot node is not immediately relieved. |
| Membership | Every caught-up node is a voter; target small clusters. | Heartbeat, election, and replication costs grow with node count. |
| Activation metadata | Three replicas, quorum reads/writes, read repair. | Large topology changes need measured throttled handoff/recovery work. |
| Notifications | Synchronous bounded admission; exact-gateway batching, 10 ms default. | `Accepted` can still be lost before owner delivery; per-session drains may cost at very high session counts. |
| Memory | Actor state, affinities, queues, logs, and replicas stay in memory. | Long-lived populations require deployment-specific capacity budgets. |

No default live migration, persistent framework state, owner-confirmed async
push, fixed notification worker pool, or large-cluster voting committee is
added in this iteration.

## Startup And Shutdown

Replicated startup orders authority before business readiness:

1. bind configuration and cluster control transport;
2. bootstrap or join as a learner;
3. catch up and promote through joint consensus;
4. run recovery participants with the gate closed;
5. commit Ready descriptors, including actor hosts, Startup replicas, and labels;
6. obtain authority and open distributed work.

Descriptor refreshes after hotfix or Startup changes commit a new membership
view even when the member was already Ready.

Shutdown closes admission before stopping business work. An ungraceful failure
does not require durable cleanup: the surviving majority removes the old exact
incarnation after its authority window. A minority cannot remove members,
elect a valid authority, acquire/supersede Actors, or reopen its gate.

## Risk Resolution Summary

| Original concern | Resolution |
| --- | --- |
| Heartbeat loop exits on exception | Supervised retries plus quorum-proof deadline, fencing, gate, and recovery barrier. |
| Seed unavailable stops control plane | Seeds are discovery contacts; any surviving majority elects a leader. |
| Local notification depends on seed | Session locator resolves the exact gateway from a local snapshot. |
| Push `Accepted` precedes owner acceptance | Intentionally retained synchronous bounded admission; async owner confirmation remains deferred. |
| Central directory load | Membership reads are local; Actor calls use cached exact activations; lifecycle writes are partitioned. |
| Seed configuration split | Seed order has no authority; join validates one cluster incarnation and committed view. |
| Cross-node wall-clock leases | Authority uses local monotonic proof durations and exact incarnation tokens; UTC is diagnostic only. |
