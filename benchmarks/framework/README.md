# Local Framework Benchmark

This subtree implements the local, native-mode framework benchmark defined in
[`docs/framework-benchmarking.md`](../../docs/framework-benchmarking.md).

The intended version 1 command is:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1
```

The current branch has completed the neutral harness plus native Lakona and
Pinus adapters for `frontdoor.echo` and `cluster.direct`. Run the four-case
smoke comparison with:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1
```

The command prepares both adapters, owns every native server and driver
process, and writes a four-row local development comparison. The larger `v1`
suite remains intentionally unavailable until the routed cluster slice is
implemented.

Run the neutral harness tests with:

```powershell
pwsh -NoProfile -File scripts/framework-benchmark/check-framework-benchmark.ps1
```

Add `-RealAdapters` to restore both native stacks, run their driver tests, and
exercise both smoke workloads plus 32-byte and 256-byte echo payloads end to
end.

All output under `artifacts/framework-benchmark/` is local and ignored by Git.
It is development evidence only, not a publishable cross-framework result.
