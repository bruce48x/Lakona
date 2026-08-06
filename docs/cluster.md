# Cluster

Lakona cluster support is process-local game state coordinated by an ephemeral,
replicated control plane. Every node, including a single process, stores
membership state; peer hints are used only during discovery and formation.
Framework state is intentionally not persisted to Postgres.

There is no standalone local cluster-endpoint mode. `AddLakonaGameServer`
installs replicated membership even for one process (quorum one), so every
cluster route is backed by an exact committed `NodeReference`.

## Terms

| Term | Meaning |
| --- | --- |
| `NodeId` | Stable operator-facing process name, such as `data-1`. It is not a fencing token. |
| `ClusterIncarnationId` | Identity of one complete in-memory cluster lifetime. A deliberate complete restart creates a new value. |
| `NodeIncarnationId` | Identity of one process lifetime. Restarting the same `NodeId` creates a new value. |
| `MembershipViewId` | Monotonic identity of a committed membership or descriptor change. |
| `NodeReference` | Exact `(cluster, node, node incarnation)` identity used for authoritative dispatch. |
| Peer | Stable `NodeId` and endpoint hint used to discover or form a cluster. It is not a leader or current-membership declaration. |
| Actor activation | Sticky `(actor, owner reference, activation id, version)` ownership record. |

Peer lists may differ between nodes. They are discovery hints, not an
operator-assigned leader or an authoritative current-member list.

## Distributed Identity And Request Lifetime

Distributed Actor safety depends on several identities with different scopes.
They are not interchangeable. The same model applies to one-node and
multi-node deployments; a single process is a cluster whose quorum is one.

```text
ClusterIncarnationId
└─ NodeReference = (cluster incarnation, NodeId, node incarnation)
   └─ Actor activation = (ActorId, owner reference, activation id, version)

MembershipViewId says which committed cluster state justified the route.
Deadline bounds one invocation; it is not an ownership identity.
```

| Value | Stable across | Changes when | What it proves |
| --- | --- | --- | --- |
| `NodeId` | Restarts of one configured process role | Configuration changes | Operator-facing logical node name only; it is never a fencing token. |
| `ClusterIncarnationId` | Joins, leaves, and ordinary node restarts in one live cluster | Formation after complete cluster loss | The message belongs to this complete in-memory cluster lifetime. |
| `NodeIncarnationId` | Nothing beyond one process lifetime | The process restarts, even with the same `NodeId` | The target is this exact process instance. |
| `MembershipViewId` | Reads of one committed membership snapshot | A membership or published-descriptor change commits | The exact committed cluster state used for the routing decision. |
| `ActorId` | Actor destruction and recreation | The business identity changes | Which logical game object is addressed. |
| `ActorActivationId` | One materialization of an Actor | The Actor is recreated or safely superseded | The request targets this exact in-memory Actor lifetime. |
| Activation version | One committed activation-directory revision | Acquire, release/tombstone, recreation, or supersession commits a newer revision | Which ownership record is newer and whether a cached record is stale. |
| Deadline | One invocation | Every call chooses its own absolute expiry | The invocation was still eligible to enter remote execution when checked. |

The cluster incarnation prevents delayed traffic from a previous complete
cluster lifetime from entering a newly formed cluster with the same
configuration. The node incarnation prevents an old process from being
confused with a replacement that reused its `NodeId`. The Actor activation id
prevents a delayed request for a destroyed Actor from entering a newly created
Actor with the same `ActorId`. The activation version orders ownership,
tombstone, and recreation records; an activation id proves difference, while
the version proves which record is newer.

`MembershipViewId` is a committed-state watermark, not an exact-match lease.
A cross-node Actor request carries the view used to select its target. The
receiver rejects the request when its current view is older than that target
view. A receiver on a newer view may continue only when the exact target
`NodeReference` and Actor activation still match its current membership and
activation-directory state. This permits harmless membership progress without
allowing a lagging receiver or stale owner.

### Cross-Node Actor Request Proof

A generated routed Actor invocation carries:

- the target cluster incarnation, `NodeId`, and node incarnation;
- the membership view used to select that exact Ready node;
- the stable `ActorId`;
- the Actor activation id and activation version;
- the stable Actor method id; and
- an absolute deadline.

Before business mailbox dispatch, the receiving node must prove all of the
following:

1. distributed-work admission is open;
2. the deadline has not expired;
3. the current cluster incarnation matches the request;
4. the local Ready member is the exact requested node incarnation;
5. the receiver's committed membership view is not behind the request's view;
6. the current activation-directory record names that exact local
   `NodeReference`, activation id, and activation version; and
7. the current Hotfix snapshot contains the requested typed method and can
   deserialize its body.

Failure closes the request before mailbox execution. A route or activation
failure is safe to classify as definitely not executed only when rejection
happened before admission to the Actor mailbox. Once execution may have been
accepted, retry safety is indeterminate unless the business operation supplies
its own idempotency key or durable fencing rule.

### Deadline And Cancellation

The Actor deadline is an absolute `DateTimeOffset` carried on the wire. The
sender first derives a remaining timeout from it and cancels the outbound
transport operation or local response wait when that interval expires. The
receiver independently checks the same deadline before mailbox dispatch, so a
delayed frame cannot become valid merely because caller-side cancellation
failed to cross the process boundary.
Cluster hosts therefore require reasonably synchronized UTC clocks for useful
cross-node expiry decisions; ownership safety still comes from incarnation and
activation tokens rather than wall-clock time.

Deadline expiry is not a rollback mechanism. Cancelling an outbound `Ask`
stops the caller's send or wait, but the current protocol has no per-request
remote cancellation frame. Once the remote mailbox accepts the call, caller
cancellation or deadline expiry does not prove that behavior stopped or that
its effects were rolled back. A `Tell` reports `Accepted` after mailbox
admission and is likewise not removed merely because its deadline passes while
queued. Product operations that cannot tolerate an ambiguous retry must remain
idempotent or persist and compare an application-level
fencing/idempotency token.

## Formation, Admission, And Identity Conflicts

The exact `Lakona:Cluster` and `Lakona:ActorHosts` shapes belong to
[Configuration](./configuration.md#cluster). Every process starts
uninitialized, listens for cluster control traffic, and then either discovers
an established incarnation or participates in formation. There is no
operator-designated first node and no separate local hosting mode.

`Peers` contains stable node identities and endpoints used as discovery hints.
Lists may differ. Uninitialized peers exchange their known hints recursively,
canonicalize the resulting formation view, and confirm the same digest before
deterministic genesis coordination. A node never removes an unreachable known
peer merely to form a smaller cluster. If any peer presents an established
incarnation, joining it as a learner takes precedence over formation.

During a concurrent cold start, every reachable node first converges on the
same canonical `(NodeId, endpoint)` formation view. The lexicographically first
`NodeId` in that view performs genesis and becomes the initial consensus
leader. The other processes continue discovery, observe the established
incarnation, and join it as learners. Genesis coordination is only a
deterministic tie break for creating one cluster incarnation; it gives that
node no permanent role or preference in later leader elections.

Formation requires a one-to-one mapping between stable node identities and
cluster endpoints. The same `NodeId` advertised with different endpoints, or
the same endpoint advertised under different node ids, fails formation rather
than guessing which declaration is authoritative. Operators must not run two
live processes with the same `NodeId`.

A one-process deployment has no remote peers and forms a one-voter cluster.
For a multi-process cold start, configured hints must connect the intended
formation graph. Two completely disconnected graphs are indistinguishable from
two deployments and may form separate incarnations; deployments that cannot
provide a connected static graph require a shared formation authority.

After complete quorum loss, forming a fresh incarnation accepts that all
in-memory Actors, sessions, membership metadata, and reliable-push state from
the prior incarnation are gone. A surviving minority remains fenced and cannot
serve distributed work.

## Cluster RPC Composition

Configuration describes addresses and topology. `Lakona.Game.Server` owns the
node-to-node RPC implementation: TCP transport and MemoryPack serialization.
Generated applications register only their client-facing endpoint
implementations. Their exact generated startup shape belongs to
[Generation Architecture](./tool/generation-architecture.md#server-renderers);
endpoint names and settings belong to
[Configuration](./configuration.md#endpoints).

The cluster channel, transport, routing RPC, and protocol DTOs are implementation
details of `Lakona.Game.Server`; there are no separately selected cluster
adapter packages. `ClusterRpcChannel` is the single internal authority. It
validates endpoint schemes, creates pooled outgoing clients, creates the local
listener, and performs a small fixed-format protocol negotiation before the
RPC serializer sees a frame. The fixed protocol ID is
`lakona.cluster.memorypack.v2`; incompatible nodes are rejected as
connection-local failures. The negotiation adds one round trip only when a
cluster connection is established; steady messages reuse pooled clients.
When a pooled client disconnects, its exact cache entry is evicted. The next
call for that route creates one replacement shared by concurrent callers;
the framework does not reconnect in the background or replay an ambiguous RPC.

Framework protocol DTOs use MemoryPack source generation with
`GenerateType.VersionTolerant` and explicit `MemoryPackOrder` values. Remote
Actor request and result DTOs follow the same rule and must live in stable,
non-hotfix assemblies. Adding a field requires a new unused order; existing
orders must never be reassigned.

Cross-node Hotfix Actor calls use two dedicated raw cluster RPC methods for
ask and tell. Their fixed header and typed MemoryPack body are written directly
into the final RPC envelope buffer; replies use the same writer-owned path.
They do not allocate an intermediate serialized Actor payload or wrap it in
the general `ClusterMessage` protocol. Reflection is allowed only while a
Hotfix snapshot closes and caches its typed method codecs, never during
per-call encode, decode, or dispatch.

## Consensus Model And Scope

Cluster membership is a specialized Raft-style replicated state machine. It
uses terms, leader election, follower log replication, majority commit,
snapshots, and joint-consensus membership changes. It is not a general-purpose
Raft store exposed to applications and does not replicate game data.

| Concern | Membership consensus owns | Membership consensus does not own |
| --- | --- | --- |
| Nodes | Exact node incarnations, voter membership, admission, removal, and lifecycle state | Process supervision or durable node recovery |
| Capabilities | Cluster endpoint, labels, `ActorHosts`, Startup Actor descriptors, and descriptor metadata | Concrete Actor instances or their mutable fields |
| Actors | The committed member view from which eligible owners are selected | Actor activation records, business state, mailbox contents, or migration |
| Connections | Exact gateway membership used to validate session locators | Sessions, reliable-push queues, callbacks, or connection state |
| Applications | Nothing from application databases | Database rows, timers, jobs, or product decisions |

Actor placement crosses two distinct coordination mechanisms. Membership
consensus supplies the committed Ready/`ActorHosts` candidate set. A placement
selector chooses an initial candidate, then the replicated activation directory
commits concrete sticky ownership through its own partition-majority protocol.
Actor activation acquisition therefore does not append one entry per Actor to
the membership Raft log.

## Replicated Membership

Every joined node automatically participates in the same in-memory membership
state machine. There is no manually assigned directory-replica role and no
cluster Postgres requirement.

### Consensus Roles And Member States

Three independent dimensions describe a node:

| Dimension | Values | Meaning |
| --- | --- | --- |
| Election role | Leader, Follower, Candidate | Transient role within one consensus term. A higher term or election can change it. |
| Replication position | Voter, Learner | Whether the exact incarnation counts toward membership quorum. |
| Lifecycle state | Joining, Recovering, Ready | Whether the node is being admitted, proving recovery, or eligible for distributed work. |

There is no permanent “follower node” type. In a stable term, ordinary
non-leader voters are followers; a follower may later become a candidate and
then leader. A learner follows the leader to catch up but does not vote until
joint-consensus promotion commits. Lifecycle readiness is independent:
becoming a voter does not open business admission until recovery succeeds and
the Ready descriptor commits.

A joining node:

1. creates a fresh `NodeIncarnationId`;
2. contacts any known peer and follows the current leader;
3. installs the committed snapshot and log tail as a non-voting learner;
4. is promoted through joint consensus after catch-up;
5. runs recovery while distributed admission remains closed;
6. commits its Ready descriptor and opens admission only after authority is
   proven.

### Leader-Only Ingress And NotLeader

Join admission, learner promotion, and Ready-descriptor commit are mutations
that only the committed voter leader may apply. Any node that receives one of
these requests but is not the leader returns a `NotLeader` protocol result
instead of executing the mutation:

- with the leader endpoint attached when the node knows the current leader;
- without an endpoint when the node does not yet know the leader.

Nodes never proxy these requests to the leader on the caller's behalf, so a
stale or fabricated hint cannot form a server-side forwarding chain. The
caller follows the attached leader endpoint at most once per retry round and
otherwise treats `NotLeader` as a normal, retryable outcome that feeds the
existing formation or promotion backoff. `NotLeader` without an endpoint is
expected while a freshly formed cluster has not yet elected its leader through
the authority control loop. It may continue to the next configured contact;
after following one endpoint hint, another `NotLeader` or a transport failure
ends the round for backoff. This rule was formally revised after three-node
startup evidence showed that stopping on the first unknown-leader response can
prevent convergence. The `RequireLeadership()` safety guard remains the final
protection and is not part of the routing contract.

Before a process has published its formed membership node, non-formation
control ingress (append, vote, proof, and snapshot traffic) returns the typed
`MembershipUnavailable` result. It carries no leader hint and means only that
the caller should use its existing bounded control-loop backoff; it is not a
handler exception, forwarding instruction, or relaxation of quorum fencing.

Membership snapshots contain exact node references, lifecycle state, cluster
RPC endpoints, actor-host descriptors, Startup descriptors, labels, and opaque
metadata on those descriptors. High-cardinality Actor activations and sessions
do not enter the global membership log.

Every caught-up member is currently a voter. This deliberately targets small,
normally odd-sized clusters. Leader heartbeat, replication, election, and
majority work grow with member count. A bounded automatic voting committee is
not part of the contract and requires measurements that justify its
complexity. Operators do not manually manage replica assignments.

The replicated log and snapshots are bounded and validated. Membership reads
use one atomically published local snapshot through `IClusterMembership`, so
steady discovery and exact endpoint lookup require no peer or leader round
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

Presenting a second incarnation for an existing `NodeId` is interpreted as a
replacement request, not as an additional member. A joining process cannot
replace the active leader's own stable node id. Reusing a `NodeId` concurrently
is therefore invalid deployment configuration even though the replacement path
can recover the stable slot after the old incarnation loses authority.

Cluster-node authentication and authorization are separate from this identity
and fencing design. Deployments must still isolate and protect the cluster
network.

### Unreachable Member Eviction

The cluster cannot determine that a process is permanently dead. It can observe
only that one exact node incarnation has stopped acknowledging the current
leader. Removing that incarnation is therefore an irreversible availability
decision, not a claim that the process can never recover:

- before removal, the cluster preserves the possibility that the process and
  its in-memory Actor state will return;
- after removal, the cluster prefers progress, accepts loss of that process's
  ephemeral state, and permits higher-generation Actor activations elsewhere.

Authority expiry and member eviction use separate time horizons. Quorum-proof
validity is short: it closes a disconnected node's distributed-work admission
gate before that node can overlap a replacement owner. The member-eviction
grace period is longer: it defines how long the cluster preserves the old
incarnation and its inaccessible in-memory state before choosing availability.
The eviction grace period is framework-owned policy, is currently one minute,
must remain longer than quorum-proof validity, and is not public
`Lakona:Cluster` configuration.

Only the current leader may initiate automatic eviction. It tracks the last
valid current-term consensus response from each exact voter. A response before
the grace period expires clears the suspicion; the member catches up and passes
the recovery barrier without changing incarnation. A newly elected leader
starts a fresh grace period rather than inheriting another process's local
failure-detector timestamp. This may delay eviction during leadership churn but
cannot discard state early because of an unverifiable clock value.

After the grace period, the leader may propose removing the exact incarnation
through joint consensus. Time alone never creates authority: the old
configuration must still have a majority capable of committing the removal. A
three-voter cluster with two connected voters can remove the third; one
survivor of a two- or three-voter cluster cannot remove the missing voters or
form a new authoritative cluster.

Once removal commits, the old incarnation is permanently fenced even if its
network recovers later. It must not resume as a follower or expose its old
Actors. The host remains unready and stops; a subsequent process start creates
a fresh `NodeIncarnationId` and joins as a learner. Actor state on the removed
process is lost unless the application can reconstruct it from application
persistence.

An unreachable member without a replacement follows the same joint-consensus
removal path. A joining process that presents the same stable `NodeId` remains
the explicit replacement path and waits through the shorter authority window
before removing the old incarnation.

## Heartbeat Failure, Fencing, Gate, And Barrier

The heartbeat/control loop is supervised. A transient exception cannot silently
terminate it: failures are observed, retried with bounded backoff, and reflected
in authority state. The safety decision is based on recent quorum proof rather
than on whether one asynchronous loop happened to be running.

### Failure And Recovery Matrix

| Event | Authoritative result |
| --- | --- |
| Leader loses contact while the other voters retain a majority | Its quorum proof expires and its admission gate closes. The connected majority elects a leader for a higher term. |
| Old leader reconnects before its incarnation is removed | It observes the higher term, becomes a follower, catches up, and passes the recovery barrier before reopening admission. |
| Old leader reconnects after removal committed | Its exact incarnation remains fenced. The host stops and a later process start joins with a new incarnation. |
| Non-leader voter disconnects briefly | It closes admission when authority expires but remains a member. On return it follows the leader, repairs its log and view, and recovers without changing incarnation. |
| A member remains unreachable beyond the eviction grace period | A leader with the old configuration's majority may joint-remove the exact incarnation. Actor ownership may be superseded only after that removal commits. |
| No partition retains a majority | No side may commit membership changes, acquire or supersede Actor ownership, or reopen distributed admission. Waiting longer does not create authority. |
| Every prior member is lost and a fresh cluster is deliberately formed | The new cluster receives a new `ClusterIncarnationId`; all framework-owned in-memory state from the previous incarnation is gone. |

Elapsed wall time has no special recovery meaning. A node returning after five
minutes follows the “before removal” or “after removal” rule according to the
committed membership view, not according to the number five. Leadership is
also not restored by identity: a former leader returns as a follower of the
current higher term.

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
The exact identity scopes, membership-view watermark, request validation, and
deadline boundary are defined in
[Distributed Identity And Request Lifetime](#distributed-identity-and-request-lifetime).

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
three Ready members by canonical rendezvous hashing. Acquire and release commit
to a replica majority. An authoritative cold read contacts every member that the
committed membership view still marks Ready, selects the unique highest version,
and repairs the current rendezvous replicas. A missing local copy means "not
learned" rather than "deleted", so newly added members cannot outvote an older
valid record with `null`. If every Ready member cannot participate, or two
different records claim the same highest version, the read fails closed.

Release commits a higher-version tombstone instead of physically deleting the
record. Public resolution still returns no Actor, while the replication layer
retains proof that older activations are fenced. The tombstone is propagated to
every current Ready member because any one of them may hold a copy from an older
replica set. Recreating the same Actor replaces that tombstone with a new
activation id and a still higher version. Repeated player login/logout therefore
updates one directory entry per node that has observed the Actor; it does not
append an unbounded per-login history.

The retained population is observable on every process through the
`Lakona.Game.Actor` meter: `lakona-actor.activation.active` counts live
ownership records, `lakona-actor.activation.metadata` counts all retained
records, and `lakona-actor.activation.released` counts retained tombstones.
The gauges deliberately carry no Actor, type, or partition tags. Lakona does
not evict fencing metadata or impose a universal record limit; deployments
should alert on the metadata gauge and its growth rate according to their own
memory budget.

Active records are also propagated to their exact owner when it is outside the
three partition replicas. Adding nodes does not move Actor ownership. Every node
is eligible automatically; there is no special peer, directory node, or Postgres
table. If the framework cannot reconcile every currently Ready member during a
cold lifecycle decision, it waits for membership to remove or recover that exact
member instead of risking a second activation.

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

Live Actor migration and automatic rebalance are unsupported because they
require an application-owned snapshot contract and mailbox barriers.

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
No peer or route-directory network lookup is required. A malformed locator or
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

`_routes.ResolveAsync` is an internal routing seam.
`MembershipSessionRouteDirectory` decodes the session locator and checks the
local membership snapshot. It does not query a configured peer or perform a
distributed directory lookup.

### Synchronous Admission Is Intentional

Generated notification methods remain synchronous for high-frequency Room
broadcasts. `Accepted` means the producer-local bounded queue owns the complete
command; it does not mean the remote gateway or client has accepted it.
Changing this default to owner-confirmed async delivery would add a route/network
wait to every push. Such a mode requires a separate measured contract.

The queue never overwrites, coalesces, or deduplicates an older accepted
notification. Those are business semantics, not framework policy.

Remote batches are keyed by exact gateway reference. The default maximum wait
is 10 ms and can be set to zero. Count and byte limits flush a batch early; an
individual command that cannot fit the byte budget returns `Backpressure`.
The exact batching and capacity keys belong to
[Configuration](./configuration.md#notifications).

The router preserves FIFO per session with one active drain per session. A
fixed session-affine worker pool is not part of the contract and requires
large-session-count measurements. Process-local admitted queues and reliable
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

## Performance Boundaries And Risks

| Area | Current decision | Recorded risk |
| --- | --- | --- |
| Actor scale-out | Existing Actors remain sticky; only new activations use new capacity. | A hot node is not immediately relieved. |
| Membership | Every caught-up node is a voter; target small clusters. | Heartbeat, election, and replication costs grow with node count. |
| Activation metadata | Three partition replicas, owner copy, all-Ready cold reconciliation, versioned tombstones, and tag-free process-local population gauges. | Cold lifecycle reads and tombstone propagation are O(nodes); retained unique Actor ids consume memory, so alert on population and growth against a deployment-specific budget. |
| Notifications | Synchronous bounded admission; exact-gateway batching, 10 ms default. | `Accepted` can still be lost before owner delivery; per-session drains may cost at very high session counts. |
| Memory | Actor state, affinities, queues, logs, and replicas stay in memory. | Long-lived populations require deployment-specific capacity budgets. |

The cluster contract does not provide live migration, persistent framework
state, owner-confirmed async push, a fixed notification worker pool, or a
large-cluster voting committee.

## Startup And Shutdown

If a leader loses a voter response after appending a membership mutation, it
retains that exact uncommitted log entry and retries it from the control loop.
It never replaces the entry or advances commit without the existing quorum.
While recovery is in progress, Join, Promote, and Ready ingress return the
normal endpoint-less `NotLeader` transient result rather than failing their RPC
handler or creating a second proposal.

Replicated startup orders authority before business readiness:

1. bind configuration and cluster control transport;
2. form a cluster or join as a learner;
3. catch up and promote through joint consensus;
4. run recovery participants with the gate closed;
5. commit Ready descriptors, including actor hosts, Startup replicas, and labels;
6. obtain authority and open distributed work.

Descriptor refreshes after hotfix or Startup changes commit a new membership
view even when the member was already Ready.

Shutdown closes admission before stopping business work. An ungraceful failure
does not prove that the process is permanently dead. The surviving majority
follows [Unreachable Member Eviction](#unreachable-member-eviction); a minority
cannot remove members, elect a valid authority, acquire or supersede Actors, or
reopen its gate.
