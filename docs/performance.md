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

The following synchronization is intentionally narrow and should not be
changed without new measurements:

- Reliable Push uses a short owner-map lock and per-owner serialization;
  network delivery occurs outside locks.
- Actor hosting operations are serialized per Actor rather than globally.
- RPC request concurrency gates and serialized frame senders are scoped to one
  session or connection.
- The default client dispatch context publishes idle with a full memory barrier
  before checking for callbacks posted concurrently. Preserve the atomic
  ownership handoff described in [RPC ordered entry](./rpc/architecture.md#ordered-message-entry);
  replacing it with a release-only write can strand callbacks even when network
  I/O and serialized sending have already completed.
- KCP transport locks and deadline-aware update scheduling follow the
  [RPC transport contract](./rpc/transport-contract.md#kcp).
  Do not trade isolated, non-overlapping registration execution for a central
  sequential update loop without representative measurements.
- The KCP server listener inputs datagrams into each connection's bounded KCP
  receive window and signals availability, but only `ReceiveFrameAsync` removes
  and decodes the next application frame. Do not restore an eager, unbounded
  decoded-frame queue or wait for one connection from the shared UDP receive
  loop.
- KCP capacity and receive-data signals coalesce under the connection lock,
  without exception-based repeated release. A pending signal represents current
  protocol/terminal state, not a datagram count. Preserve the wake-up contract
  in [RPC transports](./rpc/transport-contract.md#kcp).
- Timer callbacks execute outside the timer scheduler lock, and the timer
  scheduler already has a dedicated performance harness.
- Cross-node Hotfix Actor calls retain typed requests until a cached
  MemoryPack codec writes directly into the final RPC envelope buffer. The
  receive and reply paths operate on owned frame slices and direct response
  writers; they do not use `Type`-based serializer reflection, `ToArray`, or
  `ClusterMessage` payload wrapping. Reintroducing any of those operations on
  this path requires a focused allocation benchmark and an architecture
  decision.
- Typed client requests, typed server responses, and typed server
  notifications use the same writer-first rule: `IRpcSerializer` writes the
  business payload directly into the final pooled envelope buffer, and push
  metadata is decoded as an owned slice of the received frame. The wire bytes
  are unchanged. This is a structural allocation invariant, not a quantified
  throughput claim; any claimed performance gain still requires a focused
  benchmark.

### Client Notification Queue

The client notification receive queue is intentionally unbounded. A slow
notification handler must not cause the receive loop to drop notifications,
disconnect the client, or block frame reception. The runtime emits
logarithmically coalesced warnings when queued notification count or retained
wire bytes cross new high-water thresholds, beginning at 256 notifications
or 1 MiB. Do not propose a bounded queue, dropping, forced disconnect, or
receive-loop backpressure again without a representative stress/soak test
that measures notification rate, handler latency, queue growth, retained
memory, request latency, and recovery after the burst.
