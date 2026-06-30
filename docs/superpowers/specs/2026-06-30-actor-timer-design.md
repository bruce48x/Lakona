# Actor Timer API And Performance Design

## Purpose

Lakona actor ticks should be obvious to new users and measurable under the
same pressure as a real-time multiplayer game server.

The current hotfix feature API lets users omit the tick callback method name:

```csharp
context.ScheduleActiveActorTicks<RoomActor>(
    TimeSpan.FromMilliseconds(50),
    TickBacklogPolicy.SkipIfPending);
```

This relies on the hidden convention that the scheduler will invoke
`TickAsync`. The convention is concise, but it is not friendly to new users and
is weak under refactoring. A reader cannot see which behavior method will run
without knowing the default value inside `HotfixFeatureContext`.

The current scheduler also has no quantitative performance coverage. Agar's
battle runtime uses a 50 ms active-room tick, and a real node can host many
rooms concurrently. Before optimizing the scheduler, Lakona needs a repeatable
benchmark that measures the current design under room counts and tick rates
that resemble the target workload.

## Goals

- Make scheduled actor tick callbacks explicit at the declaration call site.
- Remove the default `"TickAsync"` method name from the public scheduling API.
- Use `nameof(...)` in samples and documentation so the method target is visible
  and protected by normal IDE rename support.
- Preserve hotfix unload safety by continuing to store method names in
  declarations instead of retaining delegates into the reloadable hotfix
  assembly.
- Add focused quantitative timer performance tests before any scheduler
  optimization.
- Measure the current scheduler across realistic active actor counts and tick
  rates.
- Record allocation, dispatch throughput, skipped/coalesced tick behavior, and
  tick latency.
- Defer scheduler optimization until benchmark results show where the cost is.

## Non-Goals

- Do not optimize the actor tick scheduler in the same change that adds the
  first benchmark.
- Do not replace hotfix tick method names with long-lived delegates.
- Do not make `ScheduleActorTick` or `ScheduleActiveActorTicks` create actors.
- Do not change `TickBacklogPolicy` semantics.
- Do not add a production load-test harness that starts Unity clients.
- Do not make benchmark thresholds so strict that normal CI machine variance
  causes frequent failures.

## Current State

`HotfixFeatureContext` currently declares:

```csharp
public void ScheduleActorTick<TActor>(
    string actorId,
    TimeSpan interval,
    TickBacklogPolicy backlogPolicy,
    string methodName = "TickAsync")

public void ScheduleActiveActorTicks<TActor>(
    TimeSpan interval,
    TickBacklogPolicy backlogPolicy,
    string methodName = "TickAsync")
```

The resulting `HotfixActorTickDeclaration` stores:

- tick mode;
- actor type;
- actor id;
- method name;
- interval;
- backlog policy.

The stable scheduler owns timers, cancellation, active actor enumeration,
pending tick tracking, mailbox entry, and invocation through the current hotfix
dispatch table. Hotfix behavior code owns only the tick method body.

The important current cost centers are:

- one `PeriodicTimer` loop per tick declaration;
- `IActorRuntime.GetActiveActorIds(Type)` on each active-actor tick interval;
- pending tick tracking through a shared dictionary and lock;
- one `TryTell` per selected actor;
- hotfix dispatch lookup and `MethodInfo.Invoke` on each accepted tick;
- diagnostic logging on skipped or unaccepted ticks when enabled.

These are plausible performance costs, but they are not proven bottlenecks yet.
The first performance work should measure them.

## API Design

Remove the default value from both scheduling APIs:

```csharp
public void ScheduleActorTick<TActor>(
    string actorId,
    TimeSpan interval,
    TickBacklogPolicy backlogPolicy,
    string methodName)

public void ScheduleActiveActorTicks<TActor>(
    TimeSpan interval,
    TickBacklogPolicy backlogPolicy,
    string methodName)
```

All samples and docs should use `nameof`:

```csharp
context.ScheduleActorTick<MatchmakingActor>(
    "default",
    TimeSpan.FromMilliseconds(250),
    TickBacklogPolicy.Coalesce,
    nameof(MatchmakingBehavior.TickAsync));

context.ScheduleActiveActorTicks<RoomActor>(
    TimeSpan.FromMilliseconds(50),
    TickBacklogPolicy.SkipIfPending,
    nameof(RoomBehavior.TickAsync));
```

This keeps the runtime declaration shape unchanged while making the callback
visible to readers. The runtime still stores a string because a delegate would
capture a method from a specific hotfix assembly generation and could keep that
generation alive after reload.

The API should keep validating that `methodName` is non-empty and that the
loaded hotfix dispatch table has a matching extension method returning
`ValueTask` and accepting `HotfixActorTick`.

## Breaking Change Decision

This is an intentional breaking source change for hotfix feature declarations.
Lakona is still early enough that requiring explicit method names is preferable
to preserving an implicit convention.

The break should be direct and noisy:

- calls that omit `methodName` fail at compile time;
- updated docs show the explicit method in every example;
- tests assert that generated or sample feature code no longer relies on the
  implicit `TickAsync` default.

No compatibility overload should be kept. Keeping an overload with the old
default would preserve the discoverability problem.

## Performance Test Design

Add a focused test or benchmark project for actor tick scheduler performance.
The first version should benchmark the current runtime without changing
scheduler behavior.

The benchmark should model two workloads:

1. Fixed actor ticks, representing singleton scheduler actors such as
   matchmaking.
2. Active actor ticks, representing many room actors broadcasting state at a
   fixed cadence.

The active actor workload should run at least these room counts:

- 100 active actors;
- 1,000 active actors;
- 10,000 active actors, if local runtime setup remains practical.

The primary interval should be 50 ms, matching Agar room ticks. A secondary 250
ms interval should cover lower-frequency singleton work such as matchmaking.

The benchmark should use real `LakonaActorRuntime` and real
`HotfixActorTickScheduler` where practical. A fake runtime can be added only for
microbenchmarks that isolate scheduler overhead, and those tests must be named
as microbenchmarks rather than treated as end-to-end evidence.

## Measured Signals

Collect these signals per scenario:

- configured actor count;
- configured interval;
- elapsed run time;
- expected tick opportunities;
- accepted tick count;
- skipped tick count;
- coalesced follow-up count;
- mailbox-full or actor-unavailable count;
- average and percentile latency from scheduled dispatch to behavior method
  entry;
- total allocations and allocation rate;
- process CPU time where available;
- active actor enumeration time where it can be measured without distorting the
  workload;
- hotfix dispatch invocation time where it can be measured without distorting
  the workload.

Latency should use `Stopwatch` timestamps in benchmark support code, not
`DateTime.UtcNow`, so timer precision and wall-clock changes do not affect the
results.

Accepted, skipped, and coalesced counts are not currently exposed as a public
scheduler contract. The benchmark implementation may add narrow internal
measurement hooks, test log capture, or scheduler diagnostics to collect them,
but it should not create a new public timer API only for benchmarks. If the
implementation turns these signals into production metrics, they must follow
the repository's low-cardinality diagnostics rules.

The first benchmark should report numbers rather than enforce strict
pass/fail thresholds. CI can run a smaller smoke version to catch pathological
regressions. Full-scale numbers should be runnable locally by maintainers.

Benchmark runs must use a repeatable protocol:

- one warmup period before recording measurements;
- a fixed measurement duration per scenario;
- at least three recorded iterations for local full runs;
- a short CI smoke duration that exercises the path without trying to prove
  capacity;
- printed runtime metadata, including OS, CPU model when available, logical CPU
  count, .NET SDK/runtime version, build configuration, and process bitness;
- printed benchmark configuration, including actor count, interval, backlog
  policy, warmup duration, measurement duration, iteration count, and whether
  the run used a real or fake runtime.

Expected tick opportunities should be calculated as:

```txt
floor(measurement_duration / interval) * actor_count
```

For fixed actor ticks, `actor_count` is the number of fixed actor ids in the
scenario. For active actor ticks, `actor_count` is the number of active actors
observed at the start of the measured interval. The report should include both
expected opportunities and accepted/skipped/coalesced counts so maintainers can
see timer drift and backlog policy effects separately.

## Test Placement

Use a dedicated test area so performance coverage is easy to run intentionally.
Acceptable placements are:

- `tests/Lakona.Game.Server.PerformanceTests` for xUnit-based benchmarks and
  smoke checks; or
- `benchmarks/Lakona.Game.Server.Benchmarks` if the repository adopts
  BenchmarkDotNet-style benchmark projects.

The recommended first step is an xUnit performance-smoke test project plus a
manual benchmark mode. That fits the current repository shape, avoids adding a
new benchmark framework before the measurement questions are settled, and lets
CI run a bounded scenario.

`HotfixActorTickScheduler` is internal to `Lakona.Game.Server`. If the first
performance coverage lives in a new test assembly, the implementation must
either add a deliberate `InternalsVisibleTo` entry for that assembly or keep the
initial scheduler benchmarks inside `Lakona.Game.Server.Tests` under a clearly
named performance collection. The test placement must not make scheduler
internals public only for benchmarking.

If BenchmarkDotNet is added later, it should be introduced as a separate
benchmark package or project and should not become required for normal solution
test runs.

## Benchmark Scenarios

### Baseline Active Room Tick

Create N `RoomActor` instances through `IActorLifecycle.CreateLocalAsync`.
Apply one feature snapshot with:

```csharp
context.ScheduleActiveActorTicks<RoomActor>(
    TimeSpan.FromMilliseconds(50),
    TickBacklogPolicy.SkipIfPending,
    nameof(RoomBehavior.TickAsync));
```

The hotfix tick method should do minimal work and record timing. This scenario
measures scheduler and mailbox overhead, not game simulation or network
broadcast cost.

### Busy Room Tick

Create N `RoomActor` instances and use a tick method that simulates bounded CPU
work. This exercises backlog policy behavior under actor turns that approach or
exceed the configured tick interval.

The busy workload should be deterministic and CPU-local. It must not sleep,
allocate large objects, perform network I/O, or depend on Unity.

### Missing Actor Tick

Apply fixed actor tick declarations for missing actors and measure that missing
actors are skipped without accidental creation. This protects the lifecycle
contract and gives a baseline for diagnostics overhead.

### Coalesce Policy Tick

Run a fixed actor or small actor set with a tick method that stays busy longer
than the interval. Measure that `Coalesce` keeps at most one follow-up tick
pending and does not grow mailbox backlog without bound.

### SkipIfPending Policy Tick

Run the same busy workload with `SkipIfPending`. Measure skipped ticks and
confirm that pending work remains bounded.

## Reporting

Performance output should be explicit enough to compare before and after
changes:

```txt
Scenario: active-room-skipifpending
Actors: 1000
Interval: 50 ms
Duration: 10 s
Accepted ticks: 198412
Skipped ticks: 0
P50 latency: 1.2 ms
P95 latency: 4.8 ms
P99 latency: 8.9 ms
Allocated: 72 MB
CPU: 1.8 s
```

Numbers in this block are examples for output shape only. The benchmark
implementation must not bake these values in as expectations.

## Optimization Gate

Do not optimize scheduler internals until the benchmark identifies a real cost.
Potential future optimization areas include:

- caching typed hotfix tick invocation delegates in the hotfix dispatch table
  for one loaded generation instead of using `MethodInfo.Invoke` on every
  accepted tick;
- maintaining active actor indexes by actor type to avoid full runtime scans on
  every active tick interval;
- avoiding per-interval sorting for active actor enumeration when stable order
  is not required;
- reducing allocation in tick dispatch argument construction;
- grouping tick sources with identical cadence when loop count becomes a
  measured problem.

These are candidates, not part of the first implementation. Each optimization
should have a before/after benchmark run.

Any delegate-cache optimization must be generation-scoped and owned by the
current hotfix dispatch table. The scheduler must not retain delegates into a
reloadable hotfix assembly across reload. The optimization must include a
regression test that loads a hotfix generation, replaces it, releases the old
dispatch table, and proves the old collectible assembly load context can be
collected.

## Testing Requirements

Add focused tests for the API change:

- `HotfixFeatureContext.ScheduleActorTick` requires `methodName`.
- `HotfixFeatureContext.ScheduleActiveActorTicks` requires `methodName`.
- invalid blank method names still throw.
- feature scanner validation still rejects missing or malformed tick methods.
- Agar `MatchmakingFeature` uses `nameof(MatchmakingBehavior.TickAsync)`.
- Agar `BattleRuntimeFeature` uses `nameof(RoomBehavior.TickAsync)`.
- durable docs examples no longer show schedule calls that omit the method
  name, except where an explicit "old/current state" example is intentionally
  showing the removed form.
- tool or renderer tests that scan generated hotfix feature output expect the
  explicit method argument.

Add focused performance tests or benchmarks for:

- active actor ticks at 100 rooms;
- active actor ticks at 1,000 rooms;
- a larger local-only run at 10,000 rooms when not enabled by default in CI;
- fixed actor ticks for singleton actors;
- `SkipIfPending` under slow actor turns;
- `Coalesce` under slow actor turns;
- missing actor ticks do not create actors.

## Documentation Updates

Update durable docs after implementation:

- `docs/actor.md` actor tick examples.
- `docs/configuration.md` hotfix feature examples.
- `docs/hotfix/architecture.md` feature descriptor examples.
- `docs/hotfix/actor-behavior.md` authoring rules and examples.
- Agar sample tests that assert schedule declarations.

The docs should explain why `nameof(...)` is required and why delegates are not
used for hotfix tick declarations.

This file lives under `docs/superpowers/**`, which is temporary agent planning
space. When implementation is complete, durable rules must move into the
authority docs above and this completed spec should be deleted rather than kept
as history.

## Versioning And Build Tags

The API change modifies shippable package content under
`src/Lakona.Game.Server.Hotfix.Abstractions`, so implementation must bump that
package version before pushing. If performance measurement hooks or scheduler
diagnostics modify shippable code in `src/Lakona.Game.Server`, that package
version must be bumped as well.

If generated starter templates, package release constants, sample package
references, or changelog entries depend on the new package versions, update
them in the same change. This includes `Lakona.Tool` release-version wiring
when generated project output consumes the changed packages.

The explicit `methodName` requirement is a stable boundary visible to
`Server.Hotfix` source code. Agar and generated hotfix examples should receive a
new `BuildTag` when their hotfix-visible source shape changes. If the
implementation concludes that a particular sample or generated template does
not need a `BuildTag` update, that decision should be called out in the
implementation notes with the reason.

## Implementation Phases

Phase 1: API and docs.

- Remove default `methodName` values from `HotfixFeatureContext`.
- Update Agar hotfix feature declarations to pass `nameof(...)`.
- Update docs and tests that currently rely on omitted tick method names.
- Bump `Lakona.Game.Server.Hotfix.Abstractions` and any other affected
  shippable package versions under `src/**`.
- Update generated template release constants, sample references, changelog
  entries, and hotfix `BuildTag` values where the new API shape changes
  generated or sample output.

Phase 2: performance measurement.

- Add the timer performance test or benchmark project.
- Add bounded CI smoke coverage.
- Add a documented local command for full-scale benchmark runs.
- Capture current baseline output before scheduler optimization.
- Bump any shippable package versions touched by scheduler diagnostics or
  benchmark-facing runtime hooks.

Phase 3: optimization decision.

- Review benchmark results.
- Choose targeted scheduler optimizations only for measured costs.
- Require before/after benchmark numbers for each optimization.
