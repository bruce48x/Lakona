# Runtime Performance

This document is the current performance-risk register for Lakona runtime and
load-testing packages. It records evidence that still needs investigation; it
is not an implementation-history log.

Focused regression tests in the owning test projects isolate one Lakona
runtime path and guard one fix. They are distinct from the local,
framework-neutral macrobenchmark defined by
[Cross-Framework Game Server Benchmarking](./framework-benchmarking.md), which
compares complete request/response and cluster RPC paths across Lakona and
other game-server frameworks. Its publishable multi-machine profile is
specified but not implemented. Lasting behavior and lifecycle rules live in the
owning Hotfix, Session, Cluster, RPC, and Actor documents.

## Investigation Workflow

Address one risk at a time. Each investigation must:

1. Add or identify one deterministic benchmark or regression test that
   exercises the real shared path and can fail on the reported symptom.
2. For quantitative claims, record runtime, OS, CPU count, GC mode, workload
   size, concurrency, and warm-up policy with the baseline.
3. Measure the signals relevant to the claim, normally throughput,
   p50/p95/p99 latency, allocation, CPU time, and a path-specific contention or
   delayed-work signal.
4. Preserve cross-index atomicity, ordering, lifecycle, cancellation, unload,
   and protocol guarantees. Replacing a collection type alone is not evidence
   that those guarantees survived.
5. Implement and verify one fix without folding unrelated behavior into the
   measurement.
6. Move lasting rules into the owning authority document and remove the
   completed entry from this register.

The [RPC reliability performance probe](../benchmarks/rpc-reliability/README.md)
compares a pinned baseline with a working-tree snapshot over real KCP and TCP
RPC connections. It records small-message throughput and latency distributions,
large-message completion time, and per-process memory, allocation, and CPU
counters. Its local closed-loop results are development evidence; the retained
run artifacts include environment and workload details and independent-run
variation.

Risk statuses are:

- **Candidate**: static evidence exists, but no repeatable measurement has
  confirmed impact.
- **Measured**: a repeatable benchmark demonstrates material impact.
- **Fixing**: an isolated implementation and regression benchmark are active.
- **Accepted**: measured impact is intentionally accepted with a documented
  bound or deployment constraint.

## Open Risks

The current KCP tuning evidence is retained in
`artifacts/rpc-reliability/kcp-tuning`: immutable pre-tuning and post-tuning
sources, hashes, build logs, replay drivers, raw results, `summary.json`, and
`report.html`. The baseline already includes RELIABILITY-022 and the signal /
client-dispatch fixes. Measurements use Windows, .NET 10.0.11, 16 logical CPUs,
Release, workstation GC, MemoryPack, separate processes and IPv4 loopback on a
shared workstation. Small cases use 3 seconds warm-up, 15 seconds measurement
and three repetitions. The initial candidate alternated with the baseline; the
final growth refinement was rerun against that preserved baseline. 12 MiB uses
three repetitions per direction,
and 24 MiB one per direction. These are closed-loop local observations, not
network-capacity guarantees. The bounded send and receive contracts are unchanged.

- **Accepted — KCP idle scheduling allocation versus active latency.** The
  20 ms update / 128 send-window default improves the measured small-message
  latency and throughput and large-message time. In a separate one-process
  probe with 200 idle connection pairs, five seconds of idle scheduling allocated
  about 4.3 MiB, versus 1.7 MiB at the previous 100 ms / 32 defaults. The 10 ms
  candidate allocated about 5.2 MiB. CPU-time samples were too coarse to claim
  unchanged CPU cost. High idle-connection counts require deployment-specific
  validation; applications may explicitly select a longer update interval.
  Do not remove per-connection scheduling isolation merely to reduce this cost.
- **Accepted — contiguous large-frame receive memory and GC pressure.**
  Incremental growth and business deserialization still allocate in proportion
  to payload size. The optimized receiver's sampled managed occupied-memory
  peak increase was about 32 MiB for 12 MiB payloads and 74 MiB for 24 MiB,
  excluding the preallocated business payload. This includes garbage awaiting
  GC, not just live data; it is neither a retained-buffer nor a process-RSS
  bound. Completed large accumulators transfer their storage and no longer
  retain a jumbo array on the connection. A separate three-upload probe reduced
  receiver allocation from about 107 to 97 MiB and post-GC managed memory from
  about 61 to 13 MiB with the connection still open. Larger growth steps reserve
  more spare capacity during a partial large frame, within the same frame limit.
  Avoid upfront allocation from an
  untrusted length prefix or unbounded decoded-frame queues as a shortcut.

## Reviewed Patterns Not Currently Listed as Risks

### LoadTesting Latency Summaries

`LoadRunner.RunAsync` owns every virtual user it starts. Before each start it
checks the run's cancellation token, including when ramp-up is zero. Planned
expiry stops scheduling, cancels active users and returns a summary after all
started users finish. Caller cancellation follows the same drain path, then
throws `OperationCanceledException` carrying the caller's token, regardless of
whether cancellation happened before startup, during ramp-up or after startup.
Scheduling failures also cancel and drain started users before propagating.
Individual scenario failures retain the existing failure statistics and do not
stop other users. Scenario tasks must await their own resource cleanup and
cooperate with cancellation; the runner does not impose a cleanup timeout or
return while started tasks are still running. From LoadTesting 0.1.4, callers
that previously received a partial summary after external cancellation must
handle cancellation instead. Normal duration expiry still returns a summary.

`Lakona.Game.LoadTesting` includes every successful operation in its latency
distribution, throughout ramp-up and the rest of the run. Failed and canceled
operations retain separate counts and do not contribute latency samples.
P50/P95/P99 use nearest ranks in a fixed histogram per operation name: ticks
below 64 are exact, and larger values use 32 buckets per power of two. A
percentile reports the bucket's inclusive upper bound, never below the observed
rank and less than 3.125% above it. Zero and `TimeSpan.MaxValue` are supported;
negative successful-operation durations are rejected before recording.

The average uses original ticks, rounded down to a tick, with a two-word sum
that avoids overflow when multiple large durations are recorded. Recording is
thread-safe and allocates nothing after an operation name is initialized.
Each name has 1,888 64-bit bucket counters (15,104 bytes of counter storage),
independent of request count or run duration. Memory still grows with the
number of distinct operation names; use stable names, not request identifiers.
Summary creation allocates a fixed-size snapshot per name. Create the final
summary after all writers finish, as `LoadRunner` does; it is not an atomic
live-progress snapshot.

The [recording benchmark](../benchmarks/load-recording/README.md) compares the
real recorder against a pinned pre-fix source revision. It measures recorder
cost and allocations, independently of RPC transport and application latency.

### Runtime Synchronization

Keep changes to synchronization and buffer ownership scoped to one measured
path. Runtime guarantees belong to the guides below; this register retains
the evidence needed to reconsider their cost.

| Path | Contract owner | Performance evidence or reconsideration check |
| --- | --- | --- |
| Reliable Push | [Sessions](session.md#reliable-push-and-resume) | Measure contention and delivery latency before widening lock scope. |
| Actor execution | [Actor turns](actor.md#actor-turns-and-hotfix-generations) | Measure mailbox throughput and cross-Actor interference. |
| RPC admission and send lifetime | [RPC lifecycle](rpc/architecture.md#host-and-session-lifetime) | Measure overload and stalled-send behavior. |
| Client dispatch handoff | [Ordered message entry](rpc/architecture.md#ordered-message-entry) | Retain a deterministic concurrent-post regression alongside scheduling measurements. |
| KCP synchronization and scheduling | [KCP contract](rpc/transport-contract.md#kcp) | Use the [reliability probe](../benchmarks/rpc-reliability/README.md); include idle and active connections. |
| Timer scheduling | [Actor timers](actor.md#timers) | The [wakeup benchmark](../benchmarks/timer-wakeup/README.md) compares ordinary and gated registration bursts; full Hotfix callback cost needs the timer performance suite. |
| Cross-node Actor encoding | [Cluster RPC](cluster.md#cluster-rpc-composition) | Measure allocations across request encoding, receive and reply paths before introducing extra copies or reflection. |
| Cross-node client-cache hits | [Cluster client ownership](cluster.md#cluster-rpc-composition) | The [cache benchmark](../benchmarks/cluster-client-cache/README.md) covers exact-route and formation-contact lookups, including concurrent hits; it does not measure end-to-end RPC latency. |
| Typed RPC frame writing | [Frame ownership](rpc/architecture.md#frame-ownership) | Removing a payload allocation is structural evidence; throughput gains still need a benchmark. |

### RPC Route Metrics

Route classification and metric labels follow
[Observability](observability.md#instrumentation-scopes).
The [route-metrics benchmark](../benchmarks/rpc-route-metrics/README.md) compares
recording cost and real 32-byte loopback requests against a pinned pre-fix runtime,
both with and without metric listeners. Loopback measurements include dispatch
and framing, but do not represent network latency or exporter cost.

### Session Population Collection

Collection freshness and failure semantics follow
[Observability](observability.md#instrumentation-scopes).

The [population benchmark](../benchmarks/session-population/README.md) compares
direct scans, forced refreshes and cache hits against a pinned baseline. It also
measures heartbeat and disconnect/rebind operations with and without concurrent
metric collection. Cache-hit measurements must not stand in for refresh cost.

### Application HTTP Body Reads

Buffering changes must preserve the raw-body lifetime and validation contract in
[Application HTTP](http.md#hotfix-owns-product-behavior).

The [body-reader benchmark](../benchmarks/http-body-reader/README.md) compares
the actual reader method with a pinned pre-fix revision, covering empty, small,
and larger bodies with known or unknown lengths and segmented reads. It measures
warmed allocation and buffering cost; it does not measure HTTP network latency
or retained process memory.

### Client Notification Queue

[RPC](rpc/architecture.md#host-and-session-lifetime) owns the queue's delivery
policy and warning thresholds. The performance tradeoff deliberately favors
response reception over a fixed notification-memory bound. Reconsidering it
requires a representative stress/soak test that measures notification rate,
handler latency, queue growth, retained memory, request latency, and recovery
after the burst; neither a bounded queue nor dropping should be presented as
a cost-free replacement.
