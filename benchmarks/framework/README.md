# Local Framework Benchmark

This subtree implements the local, native-mode framework benchmark defined in
[`docs/framework-benchmarking.md`](../../docs/framework-benchmarking.md).

The intended version 1 command is:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1
```

Version 1 compares native Lakona and Pinus paths for `frontdoor.echo`,
`cluster.direct`, and `cluster.routed`. Run the six-case smoke comparison with:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1
```

The command prepares both adapters, owns every native server and driver
process, and writes a six-row local development comparison. It requires
PowerShell 7, the .NET 10 SDK, Node.js, and npm; the entry point checks these
prerequisites before starting work.

Use the 48-case version 1 matrix for a deliberate acceptance run:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1 -Suite v1
```

The smoke suite remains the recommended fast feedback command. Narrow either
suite with `-Framework lakona|pinus` or
`-Workload frontdoor.echo|cluster.direct|cluster.routed`. After a successful
prepared run, `-NoPrepare` reuses existing build output; if output is missing,
the command explains how to recover.

## Results and interruption

Each invocation creates a new run directory below
`artifacts/framework-benchmark/` and prints that exact path. The directory
contains the run manifest, summary, validation results, process logs, raw
histograms, per-case commands/results, and `report.md`. Repeating a command
keeps suite and case identities stable while assigning a new run identity.

The report records material runtime and host metadata and shows RPS and p50
ratios against the matching echo baseline. It does not subtract latencies,
combine workloads into an aggregate score, or rank the frameworks overall.
These are same-workstation native-mode development results, not publishable
network-cluster evidence.

Ctrl+C cancels through the coordinator, marks the run incomplete, and stops
owned driver/server process trees. Startup and driver failures follow the same
cleanup path; inspect `run-manifest.json` and the run's `logs/` directory for
the actionable failure details.

## Validation

Run the neutral harness tests with:

```powershell
pwsh -NoProfile -File scripts/framework-benchmark/check-framework-benchmark.ps1
```

Add `-RealAdapters` to restore both native stacks, run their driver tests, and
exercise all smoke workloads plus 32-byte and 256-byte echo payloads end to
end.

All output under `artifacts/framework-benchmark/` is local and ignored by Git.
It is development evidence only, not a publishable cross-framework result.
