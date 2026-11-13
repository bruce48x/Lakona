# RPC reliability performance probe

This focused local benchmark measures the RPC paths affected by RELIABILITY-022:
KCP message framing, ACK progress, and the shared serialized sender. It is not
the cross-framework macrobenchmark and does not measure Game notification
routing or Outbox throughput.

Run from the repository root with Python 3.10+ and the .NET 10 SDK:

```powershell
python benchmarks/rpc-reliability/run.py --baseline HEAD --output artifacts/rpc-reliability/my-run
python benchmarks/rpc-reliability/report.py artifacts/rpc-reliability/my-run
```

Use a **new output directory** for every source or harness change. The runner
exports the specified baseline revision and snapshots the current relevant
source files (including untracked source), then builds both in Release. It
never checks out over the working tree. `--skip-build` resumes unchanged
snapshots; already completed cases are skipped. `--suite pilot` runs a quick
smoke check; `--suite small` and `--suite large` select individual matrices.

Each case launches separate client and server processes over IPv4 loopback.
The harness reuses the existing `Lakona.Rpc.Tests` friend boundary to construct
the real session and typed response writer. It uses ordinary RPC client calls,
MemoryPack, default TCP/KCP settings, and disabled keepalive. Every response is
validated; failures are recorded separately and the remaining matrix continues.
The runner exits unsuccessfully if any case failed. Small-case result waits are
bounded to the larger of 90 seconds and the requested duration plus 30 seconds;
large-case result waits to 620 seconds. Completed cases
and failure records are both preserved on resume. Child process cleanup runs on
failure or interruption. No production runtime is modified.

Defaults: 256 B / 1 KiB payloads, concurrency 1 / 32 on one connection,
3 seconds warm-up, 15 seconds measurement, 3 independent repetitions, alternating
baseline/current ordering. One histogram per worker avoids shared hot-path
counters and per-operation sample allocation. Histograms have 1% logarithmic
resolution. These are closed-loop capacity observations, without coordinated
omission correction; they are not open-loop latency guarantees.

Large cases use the current snapshot only: 12 MiB upload/download, 3 repetitions
each on KCP and TCP, plus one 24 MiB upload and download over KCP. Upload time
includes the server's complete-payload validation and small acknowledgement;
download time includes client validation. A small-message warm-up precedes the
first large transfer in each fresh process pair. Baseline KCP is not timed for
large frames because its false-success bug makes such a comparison invalid.

Raw JSON retains latency histograms, total allocations, CPU seconds, GC counts,
and managed occupied memory / private bytes / working set sampled every 20 ms.
The payload is allocated before measurement in both processes. Memory deltas
exclude that initial payload; absolute values include it. Managed occupied
memory includes garbage awaiting collection and is not a retained-live-object
measurement. Fast transient peaks can fall between samples. No forced GC is
performed during measurement. The resource sampler uses a dedicated thread and
allocation-free native Windows memory reads; non-Windows uses Process counters.

The report includes independent-run ranges, not just a best run. Keep the
baseline commit, current source hashes, source snapshots, build logs, and raw
results together. Use the measurements as local development evidence: shared
CPU scheduling, background programs, JIT, GC, and loopback all affect results.
The small single-concurrency KCP sample is particularly sparse at default KCP
settings. Do not turn machine-specific measurements into universal test gates.

For a separate diagnostic experiment, set `RPC_BENCH_DIAGNOSTICS=1` before
launching a freshly built suite. This enables first-chance semaphore exception
counting with stack inspection, including warm-up. Never mix its timings into
the main A/B report: stack capture deliberately perturbs performance.

Use `--suite tcp-stress --seconds 60 --repeats 3` to repeat the 1 KiB TCP
workload at concurrency 32 and 128. This is a progress/integrity investigation,
not proof that an intermittent stall has been fixed. To locate a stalled stage,
set `RPC_BENCH_WIRE_TRACE=1`: each endpoint records send-start, send-complete,
receive, and server-handler counts and request-id sums, plus the most recent
256 events, every two seconds and on clean exit. It also checks that outbound
headers remain unchanged until send completion. Tracing adds locking and alters
timing, so keep these runs separate from uninstrumented results. The Python
`run_case` entry point accepts an optional `on_failure` callback for external live
inspection before child-process cleanup; diagnostic failure never turns a failed
case into a pass.

## KCP tuning and memory diagnostics

For snapshots containing `KcpTransportOptions`, `RPC_BENCH_KCP_INTERVAL` and
`RPC_BENCH_KCP_WINDOW` override the update interval and send window in both
processes. Results include the effective options. Older source snapshots compile
without the options API and retain their original defaults. Compare a snapshot
of the complete preceding repair when evaluating tuning; the original
pre-RELIABILITY-022 revision cannot reliably transfer these large frames.

`RPC_BENCH_LARGE_COUNT=3` repeats large transfers on the same connection.
The report uses each record's completed `count` across all workers: payload
size times `count`, divided by the whole batch's elapsed time. It takes the
median of these per-run rates and groups different counts and concurrency
levels separately, including their resource summaries. Tables distinguish
transfers per batch from independent runs; batch duration is not per-request
latency. Per-request percentiles remain available in the raw results.

Validate report arithmetic and generated HTML with synthetic records, without
running the network benchmark:

```powershell
python benchmarks/rpc-reliability/test_report.py
```

`RPC_BENCH_RETAINED=1` additionally records managed bytes after full collection
at the end of measurement, with connections still open. This is a separate
retention diagnostic: the timed/peak counters are captured before collection,
and the retained figure includes payloads, pools and other live process state.
It is not a process-private-memory limit or a steady-state allocation benchmark.

The compiled harness also accepts `idle 200 20 128`: create 200 connection
pairs, measure five seconds idle and five seconds with one active 256-byte echo
pair. The last two arguments select interval/window on snapshots with options.
This one-process raw-transport probe is separate from the two-process RPC matrix.

`run_case(..., relay_factory=udp_relay.Relay)` routes KCP through a local UDP
relay with 20 ms one-way delay, 0–2 ms jitter, 10 Mbit/s per direction and 0.1%
seeded packet loss. The relay rewrites the bootstrap ACK port and records drops
and peak queued datagrams. Pass a factory to override conditions. This measures
a controlled impaired local path, not Internet performance. Run diagnostics
sequentially with timing matrices to avoid competing test workloads.
