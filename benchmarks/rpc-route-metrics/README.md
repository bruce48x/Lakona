# RPC Route Metrics

This probe compares OPS-002 with the pre-fix `ec121102` runtime. It measures a
registered route, first as the four metric recordings (including the candidate's
descriptor lookup), then as real 32-byte request/response exchanges through
`RpcSession` and Loopback. It uses the existing RPC test friend assembly identity;
no production visibility or benchmark-only runtime API is added.

```powershell
pwsh -NoProfile -File benchmarks/rpc-route-metrics/run.ps1
```

The runner extracts the original `RpcSession.cs` and `RpcServerTelemetry.cs`,
substitutes them only in an isolated baseline build, and builds the working-tree
candidate separately. Other dependencies and the workload are identical. It
records the resolved baseline commit, source snapshots/hashes and raw results
under `artifacts/ops-002/benchmark`. Run against a compatible baseline; this is
not a general revision-to-revision benchmark driver.

Three process pairs alternate execution order with tiered compilation disabled.
Each process warms both paths, then runs five 500,000-operation recording rounds
and three 32,000-request Loopback rounds per configuration. Listener-on uses a
no-op `MeterListener`, not an exporter. Loopback windows of one and eight cover
sequential latency and pipelined throughput. Request framing, response validation,
dispatch and final Session drain are timed; session creation is outside the
measurement. Recording allocation is thread-local; Loopback allocation is
process-wide. Keep other build/test activity idle while measuring.

## Recorded Comparison

Windows x64, .NET 10, Release, 2026-11-09. Medians across all rounds (15 recording
samples or 9 Loopback samples per cell). Loopback times are amortized per request;
the window-eight values are not individual response latency percentiles.

| Workload | Listener | Before ns/request | After ns/request | Before/after B/request |
| --- | --- | ---: | ---: | ---: |
| Four metric recordings | Off | 67.9 | 108.3 | 168 / 168 |
| Four metric recordings | On | 75.0 | 114.4 | 168 / 168 |
| Loopback, window 1 | Off | 4,589 | 4,716 | 3,166 / 3,165 |
| Loopback, window 1 | On | 4,648 | 4,753 | 3,165 / 3,165 |
| Loopback, window 8 | Off | 2,941 | 2,950 | 2,670 / 2,662 |
| Loopback, window 8 | On | 2,901 | 2,951 | 2,667 / 2,662 |

Classification adds about 39–40 ns to the isolated recording path, with no
additional allocation. Loopback medians increased 0.3–2.8%; corresponding sample
ranges overlap (for example, listener-off/window-one was 4.43–4.95 µs before and
4.44–5.30 µs after). This run shows a small cost and no large throughput regression,
not proof of zero overhead or a production latency bound. Small process-wide
allocation differences reflect scheduling and pooled-buffer reuse; they do not
establish an allocation improvement. This probe does not measure network I/O,
exporter processing, retained memory or tail latency.

Unknown-route cardinality and original-ID diagnostics are enforced by
`RpcRouteTelemetryTests`, independently of these timing measurements.
