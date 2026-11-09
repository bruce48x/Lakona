# HTTP request body reader

Run from the repository root with PowerShell 7 and .NET 10:

```powershell
pwsh -NoProfile -File benchmarks/http-body-reader/run.ps1 -OutputDirectory artifacts/perf-005/run-1
```

The script extracts the actual `ReadBodyAsync` method from pinned commit
`f2f7ead2` and the working tree, changing only baseline access from private to
internal. Both methods compile in the same Release executable; source copies,
hashes and the resolved baseline commit accompany the results. No hand-written
baseline implementation or reflection invocation is timed.

Each case warms both readers, then measures five alternating-order rounds.
Inputs cover empty, 32 B, 4 KiB, 128 KiB and 1 MiB bodies, known/unknown content
length and unrestricted/1 KiB reads, with a 2 MiB reader limit. Small cases use 10,000 iterations per round;
larger cases use 500. Allocation counters and elapsed time exclude stream/context
construction and input validation. Exact bytes are checked before each round;
length and last-byte checks are retained in the timed loop. Pool buckets are
warmed, tiered compilation is disabled only for the benchmark process, and GC
is collected outside each measurement. Run three fresh processes and compare
medians, preserving the individual samples.

This is a synchronous memory-input microbenchmark of buffering overhead, not
Kestrel throughput, request latency or retained process memory. The segmented
case models partial read sizes, not network waits. Cancellation and genuinely
pending reads are covered by the formal `LakonaHttpBodyReaderTests` suite.
