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

Risk statuses are:

- **Candidate**: static evidence exists, but no repeatable measurement has
  confirmed impact.
- **Measured**: a repeatable benchmark demonstrates material impact.
- **Fixing**: an isolated implementation and regression benchmark are active.
- **Accepted**: measured impact is intentionally accepted with a documented
  bound or deployment constraint.

## Open Risks

None.

## Reviewed Patterns Not Currently Listed as Risks

The following synchronization is intentionally narrow and should not be
changed without new measurements:

- Reliable Push uses a short owner-map lock and per-owner serialization;
  network delivery occurs outside locks.
- Actor hosting operations are serialized per Actor rather than globally.
- RPC request concurrency gates and serialized frame senders are scoped to one
  session or connection.
- KCP transport locks and deadline-aware update scheduling follow the
  [RPC transport contract](./rpc/architecture.md#transport-and-serializer-are-replaceable).
  Do not trade isolated, non-overlapping registration execution for a central
  sequential update loop without representative measurements.
- The KCP server listener inputs datagrams into each connection's bounded KCP
  receive window and signals availability, but only `ReceiveFrameAsync` removes
  and decodes the next application frame. Do not restore an eager, unbounded
  decoded-frame queue or wait for one connection from the shared UDP receive
  loop.
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
- The client notification receive queue is intentionally unbounded. A slow
  notification handler must not cause the receive loop to drop notifications,
  disconnect the client, or block frame reception. The runtime emits
  logarithmically coalesced warnings when queued notification count or retained
  wire bytes cross new high-water thresholds, beginning at 256 notifications
  or 1 MiB. Do not propose a bounded queue, dropping, forced disconnect, or
  receive-loop backpressure again without a representative stress/soak test
  that measures notification rate, handler latency, queue growth, retained
  memory, request latency, and recovery after the burst.
