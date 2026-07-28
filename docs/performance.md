# Runtime Performance

This document is the current performance-risk register for Lakona runtime and
load-testing packages. It records evidence that still needs investigation; it
is not an implementation-history log.

Focused regression tests in the owning test projects isolate one Lakona
runtime path and guard one fix. They are distinct from the deferred,
framework-neutral macrobenchmark platform defined by
[Cross-Framework Game Server Benchmarking](./framework-benchmarking.md), which
will compare complete request/response and cluster RPC paths across Lakona and
other game-server frameworks.

The repository-wide static audit performed on 2026-07-14 has no open entries.
Its Hotfix dispatch, client notification, KCP scheduling, in-memory directory,
Game Session registry, load-recorder, cluster-client, mailbox-metric, and
diagnostics-buffer findings were closed by the runtime performance milestone
on the same date. Lasting behavior and lifecycle rules live in the owning
Hotfix, Session, Cluster, RPC, and Actor documents.

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

Statuses for future entries are:

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
- KCP transport locks remain per connection; the scheduler only guarantees
  isolated, non-overlapping update execution for each registration.
- Timer callbacks execute outside the timer scheduler lock, and the timer
  scheduler already has a dedicated performance harness.
