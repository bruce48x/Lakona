# Runtime Performance

This document is the current performance-risk register for Lakona runtime and
load-testing packages. It records evidence that deserves measurement; an entry
is not a confirmed regression until a repeatable benchmark demonstrates the
impact.

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

1. `PERF-002` KCP global update scheduler
2. `PERF-003` in-memory route directory
3. `PERF-001` in-memory Game Session registry
4. `PERF-004` load-run latency recorder
5. `PERF-005` Hotfix typed delegate cache
6. `PERF-006` cluster client cache
7. `PERF-007` in-memory node directory
8. `PERF-008` Actor mailbox queue gauge
9. `PERF-009` bounded diagnostics event buffer

The order reflects likely shared-path impact, not implementation difficulty.
Reorder only when measurements or a production trace provide stronger evidence.

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

The first benchmark must mix active heartbeats, session-item reads and writes,
bind/disconnect operations, diagnostics, and expiration at 1,000, 10,000, and
50,000 sessions. It must report latency while cleanup runs, not only steady
state throughput.

Closure requires eliminating full-map scans from high-frequency paths and
showing that cleanup does not cause a material p99 latency spike. The session,
connection, callback, and termination indexes must remain atomically
consistent.

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
