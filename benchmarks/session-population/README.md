# Session population collection

Run with PowerShell 7 and .NET 10 from the repository root:

```powershell
pwsh -NoProfile -File benchmarks/session-population/run.ps1 -OutputDirectory artifacts/perf-006/run-1
```

The probe compiles the actual registry and telemetry classes from pinned commit
`99ea5f2d`, renaming their types only, beside the current runtime assembly.
The script preserves source snapshots, hashes and the resolved baseline commit.
It builds Release, disables tiered compilation only for the probe process, and
records runtime/OS/GC metadata. Repeat in three fresh processes, without other
test or build jobs, and compare medians with individual samples retained.

For 10,000 and 50,000 active sessions, five alternating-order rounds measure:

- Direct fresh registry diagnostics (100 calls).
- Actual six-gauge `MeterListener` collection, forcing expiry via a controlled
  monotonic clock before every collection (100 calls).
- The same collection without advancing time (100 baseline calls; 10,000
  candidate cache hits). The baseline has no cache and still scans six times.

Every case warms 20 times. Setup, warmup, GC collection and session creation are
outside the measured region. Allocation uses the collecting thread's counter.
Fresh collection remains O(session count); cache hits are independent of it.

Three further alternating-order rounds compare session operations with no
collection and with six-gauge collection at 10 Hz over two seconds. These use
the real system clock, including the candidate's normal one-second reuse window.
A dedicated worker rotates through the population: 15/16 operations are active
heartbeats; 1/16 is a disconnect/rebind pair. Counters report throughput and worker
allocations separately from collector cost. One operation in 127 is timed for
P50/P95/P99 (the stride samples both operation types); the result records sample
count. Final registry state and heartbeat responses are checked.

The paired operation cases share one registry, session identities and dictionary
layout across both readers. The script generates `ProbeRegistry` from current
source, adding the old diagnostics method verbatim and a selector between the
two methods. Business-operation methods are unchanged and shared by all cases;
only the diagnostics selection and real telemetry implementation differ. This
isolates collection effects from different registry objects or compiled business
method copies. GC runs before each measurement, outside
timing, and both version order and collection/no-collection order alternate.
Each listener subscribes only to its own telemetry instance. Use `-Mode collection`
or `-Mode operations` to run the two experiments independently.

This is an in-process contention and allocation probe, not RPC latency or a
production capacity estimate. Wall-clock scheduling and GC can affect sampled
tails. It does not model many simultaneous workers, exporters or network traffic.
The direct and forced-refresh results prevent cache hits from hiding scan cost.
