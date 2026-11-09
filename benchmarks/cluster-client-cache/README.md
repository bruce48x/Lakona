# Cluster client-cache benchmark

Run from the repository root with PowerShell 7 and .NET 10:

```powershell
pwsh -NoProfile -File benchmarks/cluster-client-cache/run.ps1
```

The script extracts `ClusterClientFactory` from commit `4e0f0a68`, renames only
the class, and compiles it alongside the current factory and shared supporting
runtime. The standalone, non-packable probe uses the existing `BusinessLogic.Tests`
friend assembly. Baseline commit, source snapshots/hashes, environment and raw
results are saved under `artifacts/perf-004/benchmark`. Supply a separate
`-OutputDirectory` for independent runs.

Each process runs five alternating-order rounds per case in Release with tiered
compilation disabled. Cases cover exact-incarnation routes and formation-contact
endpoints, each with one or eight dedicated lookup threads sharing a factory.
Every round warms 100,000 lookups, then measures one million, divided evenly
between workers. A small in-memory transport performs real cluster negotiation;
there are no business requests or network round trips in the measured path.

Every hit must finish synchronously and return the original client. Each case
also asserts one transport connection and exactly one disposal after shutdown.
Measurements include elapsed time, throughput, summed lookup-thread allocation,
process CPU time and Gen0 collections. Worker timing arrays and task creation
are outside the allocation window. CPU time has OS-dependent resolution.
The reported P50/P95/P99 values are **1,000-lookup batch means**, not individual
lookup or RPC latency percentiles. Concurrent values include lifecycle-lock
contention and operating-system scheduling; retain all runs when comparing.

Formal `ClusterClientFactoryTests` cover zero-allocation hits on both entry
points, truly concurrent misses, caller cancellation, failure/retry, disconnect
replacement, late old-instance cleanup, pending-connect shutdown, and real TCP
endpoint restarts. This probe measures cache lookup cost, not application-wide
RPC throughput or end-to-end latency.
