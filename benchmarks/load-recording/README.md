# Load recorder benchmark

Run from the repository root with PowerShell 7 and .NET 10:

```powershell
pwsh -NoProfile -File benchmarks/load-recording/run.ps1
```

The script extracts the real pre-OPS-001 recorder from commit `7738a230`, changes
only its namespace, and compiles it alongside the current recorder. It records
the resolved baseline commit, candidate source snapshot and hashes, raw samples,
and environment under `artifacts/ops-001/recording`. Use a separate
`-OutputDirectory` for each independent run. The benchmark project is a standalone
probe; build it through the script so the baseline source is supplied.

Both implementations use the same delegate, operation-name lookup, worker gates,
and timing loop. Each of five rounds records twenty million successes, alternating
implementation order. Each worker warms up with 100,000 records before timing;
the baseline has already filled its first-1,024 sample buffer. Cases use one and
eight dedicated threads, with either constant 1 ms latency or a mix of 1 us,
1 ms, 100 ms, and one hour. These are recorded values, not sleeps. Release runs
disable tiered compilation to remove promotion timing from the comparison.

Results include amortized wall time per record, aggregate throughput, process
CPU time, process allocation during each measured batch, and bytes allocated
when adding an operation name. The small batch allocations include task/gate and
measurement overhead; unit tests separately verify zero allocation over one
million records after initialization. CPU time is subject to OS timer resolution.
Use medians and compare independent runs: shared-workstation scheduling can
substantially vary the contended cases. This probe measures the recorder itself,
not end-to-end RPC latency or throughput.
