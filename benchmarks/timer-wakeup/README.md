# Timer wakeup benchmark

Run from the repository root with PowerShell 7 and .NET 10:

```powershell
pwsh -NoProfile -File benchmarks/timer-wakeup/run.ps1
```

The script extracts the real scheduler at `fc023e86`, renames only its class,
and builds it alongside the current production scheduler. Both use the same
current support types and runtime. The non-packable probe uses the existing
`BusinessLogic.Tests` friend assembly to access internal scheduling APIs; it
does not introduce a public benchmark API. Source snapshots, hashes, resolved
baseline commit, environment and raw samples are saved under
`artifacts/perf-003/benchmark`. Use a fresh `-OutputDirectory` for each run.

Each process warms both implementations with 32 ordinary registrations and a
64-registration burst, then runs five alternating-order rounds in Release with
tiered compilation disabled. A single producer drives these two cases:

- Ordinary: register 128 one-shot timers one at a time, each due in 2 ms, waiting
  for dispatch completion before registering the next. Record registration cost,
  allocation, total elapsed time, delay-arm count, and dispatch lateness.
- Burst: pause the scheduler inside its first one-hour delay creation, register
  10,000 two-hour timers, and add 16 immediately due probes. Release the gate and
  record registration cost, allocation, delay-arm count, and probe lateness. The
  post-burst cost is measured from release until the last delay arm; a separate
  50 ms quiet-arming observation detects settling and is included only in the
  total elapsed/allocation measurements. `registrationP95Microseconds` is only
  sampled for the ordinary case; its burst value of zero is a placeholder.

Lateness runs from the scheduled due time to `OnDispatchStarted`. A no-op Actor
owner accepts each dispatch, isolating scheduling from mailbox and business
callback costs. This is not a measurement of complete Hotfix callback latency
or application throughput. The ordinary 2 ms case is particularly sensitive to
the operating system's timer resolution. Compare repeated runs, and retain the
raw distributions rather than interpreting one tail sample as a guarantee.
Process allocation includes probe bookkeeping and descriptor creation as well
as scheduler work. Stop verifies all due probes completed without failure and
all underlying timer handles were released.

The formal `LakonaTimerSchedulerTests` additionally execute real callbacks with
a controlled clock: burst delivery, earlier insertion during arming, wake/expiry
cleanup races, periodic rearming and shutdown drain. The existing
`LakonaTimerPerformanceTests` smoke probe covers full Hotfix dispatch.
