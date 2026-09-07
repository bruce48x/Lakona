# 4. Performance And Resource Efficiency

Treat correctness and runtime contracts as constraints, not negotiable
performance variables. Read `docs/performance.md` and
`docs/framework-benchmarking.md`.

1. Identify static risks in hot paths, allocation, serialization, copying,
   batching, queues, locks, scheduling, timers, diagnostics, and network
   protocols.
2. Use an existing deterministic benchmark or regression harness when one
   exercises the real shared path. Do not infer material impact from code shape
   alone.
3. For quantitative claims, record workload semantics, runtime, build mode, OS,
   hardware, CPU count, GC mode, payload, topology, concurrency, offered load,
   warm-up, and measurement duration.
4. Measure the relevant vector: correctness and errors, throughput, offered
   load, p50/p95/p99 and maximum latency, CPU time and utilization, working set,
   allocation and GC behavior, network bytes and packets, and path-specific
   queue, contention, or delayed-work signals.
5. Compare alternatives through Pareto dominance. Reject an alternative that
   is no better on every relevant signal. Present trade-offs among the
   non-dominated alternatives against explicit workload, latency, resource,
   cost, and deployment constraints.

Do not declare a win from one metric while hiding regressions in another. Do
not publish a default aggregate score or invent weights across unlike metrics.
Calculate a composite score only when maintainers supply the weights and
deployment objective. Do not add benchmark-only production paths, weaken
correctness, or compare workloads with different semantics.

Label a static concern `Candidate`; label it `Measured` only after a repeatable
benchmark confirms material impact. An intentionally accepted trade-off must
state its bound or deployment constraint.
