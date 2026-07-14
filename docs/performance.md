# Runtime Performance

This document is the current performance-risk register for Lakona runtime and
load-testing packages. It records evidence that deserves measurement; an entry
is not a confirmed regression until a repeatable benchmark demonstrates the
impact.

The focused benchmarks required by this register isolate one Lakona runtime
path and guard one fix. They are distinct from the deferred, framework-neutral
macrobenchmark platform defined by
[Cross-Framework Game Server Benchmarking](./framework-benchmarking.md), which
will compare complete request/response and cluster RPC paths across Lakona and
other game-server frameworks.

The last repository-wide static audit was performed on 2026-07-14. It covered
runtime code under `src/**`, excluding source text emitted by generators. The
audit found 32 runtime files containing 145 `lock` sites. Most locks are scoped
to one connection, Actor, owner, or lifecycle and are not listed here.

## Investigation Workflow

Address one risk at a time. Each investigation must:

1. Add or identify one deterministic benchmark that exercises the real shared
   path and can fail on the reported symptom.
2. Record runtime, OS, CPU count, GC mode, workload size, concurrency, and
   warm-up policy with the baseline.
3. Measure throughput, p50/p95/p99 latency, allocation, CPU time, and a
   path-specific contention signal such as delayed or skipped work.
4. Preserve cross-index atomicity, ordering, lifecycle, and unload guarantees;
   replacing a dictionary with `ConcurrentDictionary` is not sufficient by
   itself.
5. Implement and verify one fix without combining unrelated performance
   changes.
6. Update this register with the benchmark command and result. Once the risk is
   verified closed, move any lasting rule into the owning authority document
   and remove the completed entry instead of retaining implementation history.

Statuses are:

- **Candidate**: static evidence exists, but no benchmark has confirmed impact.
- **Measured**: a repeatable benchmark demonstrates material impact.
- **Fixing**: an isolated implementation and regression benchmark are active.
- **Accepted**: measured impact is intentionally accepted with a documented
  bound or deployment constraint.

## Recommended Order

1. `PERF-010` generated Hotfix request dispatch allocations
2. `PERF-011` client notification command materialization
3. `PERF-002` KCP global update scheduler
4. `PERF-003` in-memory route directory
5. `PERF-001` in-memory Game Session registry
6. `PERF-004` load-run latency recorder
7. `PERF-005` Hotfix typed delegate cache
8. `PERF-006` cluster client cache
9. `PERF-007` in-memory node directory
10. `PERF-008` Actor mailbox queue gauge
11. `PERF-009` bounded diagnostics event buffer

The order reflects likely shared-path impact, not implementation difficulty.
Reorder only when measurements or a production trace provide stronger evidence.

## PERF-010: Generated Hotfix Request Dispatch Allocations

- **Status:** Candidate
- **Priority:** P0
- **Scope:** `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`,
  `src/Lakona.Game.Server.Hotfix.Generators/ActorSelectorEmitter.cs`,
  `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`,
  `src/Lakona.Game.Server.Hotfix/Runtime/HotfixRuntimeSnapshotLease.cs`,
  `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchRuntimeScope.cs`, and
  `src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs`

Generated stable service proxies are scoped to an RPC Session rather than one
request, but the Hotfix request path performs several request-level
allocations after entering the proxy:

- A generated proxy acquires a class-based runtime lease and scope, resolves
  current Game Session state, and constructs a `HotfixServiceCall<TRequest>`
  or `HotfixServiceCall<TRequest, TCallback>` object.
- Service dispatch constructs a type array and string method key, activates a
  new non-static Hotfix service instance, invokes it through
  `MethodInfo.Invoke` with an object array, and disposes the instance after the
  returned `ValueTask` completes.
- Generated Actor selectors create parameter-type and argument arrays before
  entering reflective Hotfix behavior dispatch. A high-frequency method such
  as Agar `BattleService.SubmitInputAsync` therefore pays both the service and
  Actor dispatch costs.

The stable service proxy, request DTO, serializer buffers, user-created
business objects, and Actor mailbox work item are different lifetimes and must
not be attributed to the Hotfix dispatch overhead without measurement.
Likewise, the eager session-items snapshot is owned by `PERF-001` even when a
generated proxy exposes it on the call context.

The first benchmark must include a direct typed-call control and these warmed
Hotfix scenarios:

- a no-op instance service method whose `ValueTask` completes synchronously;
- the same method forced to complete asynchronously;
- generated proxy dispatch with no current Game Session;
- generated proxy dispatch with a current session and four session items; and
- a Battle-like service-to-Actor call using deterministic in-memory runtime
  adapters.

Run each scenario with 1, 2, 4, 8, and 16 or more workers, including one count
at or above the machine CPU count. Report operations per second,
allocated bytes per operation, Gen0 collections, CPU time, and p50/p95/p99
latency. Also report service constructor and disposal counts, generated method
key lookups, and the result of a reload-under-load test followed by a
collectible load-context unload check.

Closure requires one service instance per published Hotfix generation by
default, a generated numeric-slot typed invocation path without per-call
string keys, type arrays, object arrays, or reflection, and a readonly request
context containing only request-scoped data. Constructor dependencies must
remain generation-owned, one request must observe one generation, and the old
service provider and load context must be collectible after in-flight calls
drain. The warmed synchronous dispatch control must allocate no heap bytes
attributable to framework dispatch; genuinely asynchronous work may retain its
required completion state but must not recreate the removed dispatch objects.

Generated Actor behavior dispatch must meet the same typed-invocation and
unload guarantees before the end-to-end service-to-Actor scenario is closed.
Mutable request state must remain local to the call; generation-scoped service
instances are concurrent coordinators, while durable mutable state belongs in
Actors, Game Sessions, or an explicitly synchronized state module.

## PERF-011: Client Notification Command Materialization

- **Status:** Candidate
- **Priority:** P0/P1
- **Scope:** `src/Lakona.Game.Server/Sessions/ClientNotifications.cs`,
  `src/Lakona.Game.Server/Sessions/ClientNotificationCommandFactory.cs`, and
  generated callback notification adapters

`IClientNotifications.ForSession` creates a target object for every selected
session. Typical business calls then create a capturing callback delegate.
`ClientNotificationCommandFactory` creates a `DispatchProxy`, captures a
reflective invocation, builds argument and command objects, and serializes the
arguments before dispatch. The Agar `MatchmakingNotifier` and `RoomNotifier`
objects themselves are Hotfix-generation singletons; the candidate risk is
the per-notification command path, not their object lifetime.

The first benchmark must compare a direct typed callback control with the
current notification API for local best-effort, local reliable, and remote
routed delivery. Cover 1, 8, and 32 or more concurrent publishers and fan-out
to 1, 10, and 100 sessions. Report notifications per second, allocated bytes
per notification, Gen0 collections, CPU time, p50/p95/p99 publish latency,
serialized bytes, and delivery status counts. Remote transport latency must be
replaced with a deterministic in-memory adapter so command construction and
routing remain visible.

Closure requires generated typed notification commands or an equivalent
compile-time adapter that removes the per-send target class, captured lambda,
`DispatchProxy`, reflection, and argument-list construction. Local,
reliable/replayable, and remote delivery must preserve ordering, callback
contract validation, route generation, cancellation, and status semantics.
The framework may still allocate the payload representation required for
serialization or replay, but local delivery must not serialize solely to
rediscover a callback method already known at compile time.

## PERF-001: In-Memory Game Session Registry

- **Status:** Candidate
- **Priority:** P1
- **Scope:** `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`

`InMemoryGameSessionRegistry` is a singleton. One `Lock` protects all sessions,
connection mappings, callback mappings, heartbeat state, and session items.
Short dictionary operations therefore serialize across unrelated sessions.

Several operations also perform work proportional to the total session count
while holding the same lock:

- `StartNewSessionAsync` scans all session keys to calculate owner generation.
- `RecordHeartbeatAsync` scans all sessions on its terminated-connection
  fallback path.
- `GetDiagnosticsSnapshot` scans all sessions.
- `ExpireDisconnectedSessionsAsync` copies and scans the complete session map.

Generated Hotfix service proxies call `GetCurrentSessionAsync` and then
`GetSessionItemsAsync` before dispatch. When a session has items,
`GetSessionItemsAsync` constructs a new `GameSessionItems` and copies the
complete item dictionary while holding `_gate`. High-frequency request
benchmarks must therefore distinguish registry/session-snapshot allocation
from the dispatch allocations tracked by `PERF-010`.

The first benchmark must mix active heartbeats, individual and full-snapshot
session-item reads, session-item writes, bind/disconnect operations,
diagnostics, and expiration at 1,000, 10,000, and 50,000 sessions. It must
report latency and allocation while cleanup runs, not only steady-state
throughput.

Closure requires eliminating full-map scans from high-frequency paths and
showing that cleanup does not cause a material p99 latency spike. The session,
connection, callback, and termination indexes must remain atomically
consistent. A generated request path that needs session context must be able to
read one atomically consistent session-and-items snapshot without copying the
item dictionary for every request.

## PERF-002: KCP Global Update Scheduler

- **Status:** Candidate
- **Priority:** P0
- **Scope:** `src/Lakona.Rpc.Transport.Kcp/Runtime/KcpUpdateScheduler.cs`

Every client and server KCP transport registers an update callback with one
static scheduler. A single `Timer` runs every 10 milliseconds and invokes all
callbacks sequentially. Each callback then acquires its transport's KCP lock.
One busy or contended connection can therefore delay every other connection,
and `_tickRunning` drops overlapping ticks instead of recording or recovering
the delayed work.

The first benchmark must cover 100, 1,000, and 10,000 idle and active
connections. It must measure complete tick duration, skipped ticks, per-
connection update delay, CPU time, send latency, and the effect of one
deliberately slow connection.

Closure requires bounded update delay as connection count grows and isolation
such that one slow connection cannot stall all KCP updates. Any sharding or
scheduling change must preserve single-threaded access to each KCP instance.

## PERF-003: In-Memory Route Directory

- **Status:** Candidate
- **Priority:** P1
- **Scope:** `src/Lakona.Game.Cluster/Routes/InMemoryRouteDirectory.cs`

The directory seed registers one singleton `InMemoryRouteDirectory`. Every
route registration, resolution, lease refresh, and removal acquires one global
lock. Client notification routing calls `ResolveAsync` on the delivery path.

Expiration and node cleanup are more expensive: `ExpireAsync`,
`ClearByNodeAsync`, and `ClearByNodeEpochAsync` scan the full route dictionary,
allocate a key array, and remove entries while holding the same lock used by
route resolution.

The first benchmark must combine at least 90% route resolutions with lease
refresh, registration, expiration, and node cleanup at 1,000, 10,000, and
100,000 routes. It must measure resolution latency during cleanup.

Closure requires route reads to remain available during large cleanup passes,
without removing a concurrently refreshed route or violating node-epoch and
generation semantics. Secondary indexes by node may be evaluated, but are not
preselected as the solution.

## PERF-004: Load-Run Latency Recorder

- **Status:** Candidate
- **Priority:** P1 for measurement accuracy
- **Scope:** `src/Lakona.Game.LoadTesting/Internal/LoadRunRecorder.cs`

All successful samples for the same operation name acquire one
`OperationAggregate` lock. Only the first 1,024 latencies are retained, but
every later success still enters the lock to discover that the buffer is full.
The load generator can therefore become the bottleneck in the system it is
supposed to measure.

The first benchmark must record one operation from 1, 2, 4, 8, and 16 or more
parallel workers before and after the sample buffer fills. It must report
recorder throughput and the overhead added to a no-op scenario.

Closure requires bounded, low recorder overhead after the buffer is full and a
race-free snapshot containing no more than the configured sample capacity.

## PERF-005: Hotfix Typed Delegate Cache

- **Status:** Candidate
- **Priority:** P1/P2
- **Scope:** `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`

`ResolveDelegate` protects its delegate dictionary with one lock. Cache hits
still acquire the exclusive lock. `HotfixCall` and generated
`<State>HotfixCaller.Call` methods use this typed path, so concurrent calls on
unrelated states can serialize on one dispatch table.

This finding does not describe every Hotfix dispatch path. Generated Actor
behavior methods that use the reflective `InvokeValueTaskAsync` path must be
measured separately rather than attributed to this lock.

The first benchmark must compare cold and warm cache behavior for one and many
method keys across increasing worker counts. It must include a Hotfix reload
and collectible load-context unload check.

Closure requires warm cache hits without a shared exclusive lock and no
delegate or type retention after the owning Hotfix runtime is retired.

## PERF-006: Cluster Client Cache

- **Status:** Candidate
- **Priority:** P2
- **Scope:** `src/Lakona.Game.Cluster.Rpc/Clients/ClusterClientFactory.cs`

`ClusterClientFactory` is a singleton, and every cluster send calls
`GetClientAsync`. Even a cache hit acquires one lock shared by all target nodes.
Cache misses connect outside the lock, which avoids blocking cached calls but
allows duplicate concurrent connections to the same node before one wins the
second lock.

The first benchmark must measure warm cache hits and concurrent misses for one
and many nodes. Network cost must be removed or replaced with a deterministic
in-memory transport so cache synchronization remains visible.

Closure requires scalable cache hits and at most one retained client for a
node endpoint and epoch. Superseded and losing clients must still be disposed.

## PERF-007: In-Memory Node Directory

- **Status:** Candidate
- **Priority:** P2
- **Scope:** `src/Lakona.Game.Cluster/Nodes/InMemoryNodeDirectory.cs`

Registration, heartbeat, state changes, resolution, query, and expiration use
one global lock. `QueryAsync` filters, sorts, and copies all matching nodes
inside the lock. `ExpireAsync` scans and removes expired nodes while holding
the same lock used by heartbeats.

The first benchmark must combine heartbeats with placement-style queries and
expiration at 100, 1,000, and 10,000 nodes. Although ordinary clusters may be
smaller, the measurement should identify the point where query work affects
heartbeat latency.

Closure requires bounded heartbeat latency during query and expiration while
preserving epoch, state, label, and deterministic query-order semantics.

## PERF-008: Actor Mailbox Queue Gauge

- **Status:** Candidate
- **Priority:** P3
- **Scope:** `src/Lakona.Game.Server/Internal/ActorKernel/Mailbox/Mailbox.cs`

The observable mailbox queue-length gauge enumerates every active mailbox and
sums each input count whenever metrics are collected. This path has no global
lock, but its cost is proportional to the number of live Actors and runs on the
metrics collection path.

The first benchmark must measure scrape CPU time and application latency with
1,000, 10,000, and 100,000 idle and active mailboxes.

Closure requires constant or explicitly bounded collection cost without
losing queue-count accuracy during enqueue, processing, rejection, and mailbox
shutdown.

## PERF-009: Bounded Diagnostics Event Buffer

- **Status:** Candidate
- **Priority:** P3
- **Scope:** `src/Lakona.Game.Server/Observability/Diagnostics/BoundedDiagnosticsEventBuffer.cs`

All enabled diagnostic publishers share one queue lock. Publishing performs a
bounded dequeue/enqueue operation, while `Snapshot` reverses, limits, and
copies the queue under the same lock. The default capacity is 1,024 and the
default minimum level is `Warning`, so the expected normal risk is low, but an
error storm plus repeated diagnostics snapshots may make the path visible.

The first benchmark must combine concurrent warning publication with repeated
maximum-size snapshots and measure both publisher and application p99 latency.

Closure requires the configured bound and newest-first snapshot semantics to
remain intact without material application latency during an error storm.

## Reviewed Patterns Not Currently Listed as Risks

The following synchronization is intentionally narrow and should not be
changed without new measurements:

- Reliable Push uses a short owner-map lock and per-owner serialization; network
  delivery occurs outside locks.
- Actor hosting operations are serialized per Actor rather than globally.
- RPC request concurrency gates and serialized frame senders are scoped to one
  session or connection.
- KCP transport locks are per connection; `PERF-002` concerns the global
  scheduler that visits them.
- Message recording locks one Actor's log list rather than all Actor logs.
- Timer callbacks execute outside the timer scheduler lock, and the timer
  scheduler already has a dedicated performance harness.
