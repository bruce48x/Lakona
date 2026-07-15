# Local Framework Benchmark

This subtree implements the local, native-mode framework benchmark defined in
[`docs/framework-benchmarking.md`](../../docs/framework-benchmarking.md).

The intended version 1 command is:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1
```

The current branch has completed the neutral Slice 1 harness: versioned suite
and result contracts, deterministic case expansion, child-process lifecycle,
correctness validation, histograms, and report bundles. The Lakona and Pinus
native adapters arrive in the next slice, so the public command intentionally
fails with an actionable missing-adapter message until both manifests exist.

Run the neutral harness tests with:

```powershell
pwsh -NoProfile -File scripts/framework-benchmark/check-framework-benchmark.ps1
```

All output under `artifacts/framework-benchmark/` is local and ignored by Git.
It is development evidence only, not a publishable cross-framework result.
