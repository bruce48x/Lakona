# Timer API Redesign

Date: 2026-06-30

## Context

The current hotfix timer surface is centered on `HotfixFeatureContext.ScheduleActorTick`
and `ScheduleActiveActorTicks`. That model has two problems:

- The callback is hidden behind framework convention. Even with an explicit
  `nameof(...)`, new users still have to understand that a feature declaration
  creates actor ticks through a separate scheduler.
- Runtime control is limited. A room, service, or feature that needs multiple
  timers over its lifetime cannot naturally create and destroy them as gameplay
  state changes.

The Agar sample also highlights a performance concern: many rooms may broadcast
at high frequency. The current implementation has not been quantitatively
tested enough to claim whether it is good or bad. The redesign must therefore
include both a simpler API and a measurement path.

## Goals

- Provide one public timer API that can be used from actor behavior, feature
  code, RPC services, lifecycle callbacks, and timer callbacks.
- Make timer creation and deletion explicit: create returns a framework-generated
  `TimerId`, and delete uses that id.
- Preserve hotfix reload expectations: an existing timer survives reload and
  calls the new version of the same callback method.
- Avoid holding delegates, hotfix instances, `MethodInfo`, `Type`, or other
  objects that keep old hotfix assemblies alive.
- Use a central min-heap scheduler instead of one framework loop or one
  low-level timer per logical timer.
- Add quantitative performance tests and benchmarks before deciding on deeper
  optimization work.

## Non-Goals

- No pause or resume API. Timers support create and destroy only.
- No user-specified timer ids.
- No timer serialization across process restart.
- No lambda selector API in the first version.
- No public hotfix `OnReload` hook in the first version.
- No configurable backlog policy in the first version. Timers use
  `SkipIfPending` semantics.
- No second legacy-compatible timer API. The old actor tick API should be
  removed as part of the migration.

## Public API

`LakonaTimer` is the only public entry point. The API should live in
`Lakona.Game.Server.Hotfix.Abstractions` so hotfix projects can reference it
without depending on the stable server implementation package.

```csharp
TimerId timerId = await LakonaTimer.CreatePeriodicTimerAsync<RoomTimerCallbacks, RoomBroadcastTimerArgs>(
    dueTime: TimeSpan.Zero,
    period: TimeSpan.FromMilliseconds(50),
    methodName: nameof(RoomTimerCallbacks.BroadcastAsync),
    args: new RoomBroadcastTimerArgs(roomId));

await LakonaTimer.DestroyTimerAsync(timerId);
```

The first version should expose concrete signatures:

```csharp
public static class LakonaTimer
{
    public static ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken = default);

    public static ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken = default);

    public static ValueTask DestroyTimerAsync(
        TimerId timerId,
        CancellationToken cancellationToken = default);
}
```

`TimerId` should be an opaque runtime handle. The implementation may use a
`readonly record struct` with an internal factory and `default` as invalid, or
an equivalent shape that prevents users from constructing valid ids manually.
The type does not need serialization support.

The static surface intentionally does not mean unconstrained global mutable
state. It is a facade over the current framework-managed execution scope.
`Hotfix.Abstractions` owns the facade, execution-scope holder, and minimal
backend contract. `Lakona.Game.Server` implements that backend and installs the
scope before invoking hotfix code. This avoids any reverse dependency from
abstractions back to the server implementation assembly. The abstractions
assembly must be loaded from the default/shared load context, not privately into
the hotfix load context; otherwise the static facade and `AsyncLocal` scope
would be split between two assembly instances.

## Timer Execution Scope

`LakonaTimer` resolves an internal execution scope that is installed by the
framework immediately before invoking user hotfix code and cleared in a
`finally` block after that invocation completes.

The scope is valid for:

- hotfix actor behavior dispatch
- hotfix feature `StartAsync` and `StopAsync`
- hotfix feature command dispatch
- hotfix RPC service dispatch
- hotfix lifecycle dispatch
- hotfix timer callback dispatch

The scope should be carried through normal `await` continuations. It must not
be usable after the framework-managed invocation has completed. A practical
implementation can use an internal `AsyncLocal<LakonaTimerExecutionScope>` plus
an active lease flag. The framework deactivates the lease before clearing the
ambient value so background work that captured `ExecutionContext` cannot create
or destroy timers after the originating hotfix call has returned.

Calls from ordinary application startup code, unmanaged background threads,
`Task.Run` work that executes after the hotfix invocation exits, or code that
suppresses `ExecutionContext` flow fail with `InvalidOperationException`. The
error message should say that `LakonaTimer` can only be used inside a
framework-managed Lakona hotfix execution scope.

## Callback Contract

Timer callbacks are neutral hotfix callbacks, not actor ticks. The callback type
is explicit through `TCallback`, and the callback method is explicit through
`nameof(...)`.

```csharp
public sealed class RoomTimerCallbacks
{
    private RoomTimerCallbacks()
    {
    }

    public static async ValueTask BroadcastAsync(TimerTick<RoomBroadcastTimerArgs> tick)
    {
        var rooms = tick.Services.GetRequiredService<RoomActors>();
        await rooms.Local(new RoomId(tick.Args.RoomId)).BroadcastAsync();
    }
}
```

Required callback shape:

```csharp
public static ValueTask Method(TimerTick<TArgs> tick)
```

The callback must be `public static`, return `ValueTask`, and accept exactly one
`TimerTick<TArgs>` parameter. Instance callbacks are intentionally not supported.
Services are resolved through `tick.Services`. Actor state changes must be made
by explicitly calling an actor reference from the callback so actor mailbox
ordering remains intact.

Overload resolution is strict. The `methodName` must resolve to exactly one
`public static` method on `TCallback` with the exact required signature. Multiple
methods with the same name are allowed only if exactly one matches the timer
signature; multiple exact matches or no exact match fail creation and reload
resolution.

`TCallback` must be an ordinary type, such as a sealed class. C# static classes
cannot be used as generic type arguments, so callback methods should be static
methods on a non-static type.

The first version should reject:

- generic callback types
- generic callback methods
- generic root argument types, including closed constructed generic types such
  as `List<T>` or `Dictionary<TKey, TValue>`
- callback types outside the active hotfix assembly

`TArgs` may come from the active hotfix assembly or from a stable shared
contract assembly that the current hotfix assembly can resolve. The root
argument type must be a non-generic named DTO. DTO members may use serializer
supported primitive, enum, string, array, list, and nested DTO shapes, but the
descriptor only identifies the non-generic root `TArgs` type. If the callback
type or argument type is renamed or moved after reload, the next tick reports a
missing type and skips according to the timer kind.

## Timer Context

`TimerTick<TArgs>` carries the runtime context required by callbacks:

- `TimerId`
- `TArgs Args`
- `IServiceProvider Services`
- `DateTimeOffset DueAtUtc`
- `DateTimeOffset ObservedAtUtc`
- `CancellationToken CancellationToken`

The exact property names can be adjusted during implementation, but the object
must not expose mutable scheduler internals.

## Hotfix Reload Semantics

Timer creation validates the callback method against the hotfix runtime snapshot
leased by the current `LakonaTimer` execution scope. This matters if an old
snapshot callback is still running after a newer hotfix has been published:
creation uses the old leased snapshot for validation and argument serialization
because that is where `TCallback` and `TArgs` came from. Future ticks still use
the stored reload-safe descriptor and resolve against the latest active
snapshot.

After creation, the framework stores a reload-safe descriptor:

```txt
TimerId
CallbackAssemblyName
CallbackTypeFullName
MethodName
ArgsAssemblyName
ArgsTypeFullName
ArgsSerializerId
ArgsPayload
NextDueTimestamp
Period
Generation
```

The framework does not store delegates, hotfix instances, `MethodInfo`, `Type`,
or raw hotfix argument objects. Arguments are serialized into in-memory payloads
when the timer is created. This is still useful without process persistence
because it prevents old hotfix object graphs from keeping old load contexts
alive and allows reload to deserialize into the new argument type.

The first version should use a dedicated timer argument serializer based on
System.Text.Json UTF-8 payloads:

- `ArgsSerializerId` is `system-text-json-v1`.
- The payload stores UTF-8 JSON bytes plus the argument assembly simple name and
  full type name.
- The scheduler must not store `JsonSerializerOptions`, `JsonTypeInfo`,
  formatter caches, or other serializer metadata resolved for hotfix types.
- Serialization and deserialization should use short-lived options or another
  implementation strategy that does not keep hotfix `Type` instances alive in a
  singleton cache.
- `TArgs` must round-trip through this serializer at creation time. Creation
  fails if serialization fails.

The callback descriptor uses the callback assembly simple name and full type
name, not a stored runtime `Type` or `MethodInfo`. Callback resolution first
looks in the current active hotfix assembly. Argument type resolution uses the
current active hotfix assembly and then the stable assemblies available to that
hotfix. Type rename, namespace move, assembly rename, or method rename after
reload are reported as resolution failures.

After hotfix reload:

- Existing timers remain registered.
- The scheduler does not recreate timers.
- The next tick resolves the callback and argument descriptors against the
  latest hotfix dispatch view.
- If resolution succeeds, the new version of the same callback method runs.
- If resolution fails, the tick is skipped and an error is reported.

Reload does not automatically update a timer's period or arguments. Code that
wants a different schedule must explicitly destroy the old timer and create a
new one.

## In-Flight Reload Semantics

A timer callback dispatch leases the hotfix runtime snapshot it resolves
against. If a hotfix reload is published while a timer callback is already
running:

- the in-flight callback continues on the old snapshot;
- `tick.Services` remains the service provider from that leased snapshot;
- reload does not cancel the callback;
- new timer dispatches resolve against the newly published snapshot;
- the old service provider and load context are disposed only after all active
  leases on that snapshot are released.

This lease model should apply consistently to timer callbacks and other hotfix
timer dispatch work that can overlap reload. It prevents a timer callback from
using a disposed service provider while also allowing new timer callbacks to
switch to the new hotfix immediately after publish. Extending the same lease
model to every non-timer hotfix entry point is a separate hotfix runtime
hardening task, not a requirement of this timer API spec.

Shutdown is different from reload. Server shutdown cancels the scheduler and
the active timer tick cancellation tokens, then waits for in-flight callbacks up
to the normal host shutdown budget before disposing runtime services.

## Error Handling

Creation fails immediately for invalid inputs:

- `dueTime < TimeSpan.Zero`
- periodic `period <= TimeSpan.Zero`
- missing callback method
- non-static callback method
- wrong return type
- wrong parameter shape
- non-serializable arguments

Runtime tick failures are reported and skipped:

- callback type missing after reload
- callback method missing after reload
- callback signature mismatch after reload
- argument deserialization failure
- callback exception

Periodic timers are not automatically destroyed after a runtime tick failure.
The next period attempts dispatch again.

One-shot timers complete their lifecycle when their due time is reached. If the
callback cannot be resolved, arguments cannot be deserialized, or the callback
throws, the tick is reported as skipped or failed and the one-shot timer is
removed because it has expired. This is not an error-triggered auto-delete; it
is the natural completion rule for one-shot timers. Code that wants retry
behavior should create a periodic timer or create a new one-shot timer from
business code.

## Backlog Policy

The first version has one built-in backlog policy: `SkipIfPending`.

If a callback for the same `TimerId` is still running when another due time
arrives, the new tick is reported as skipped and is not queued. This protects
high-frequency timers from unbounded work growth.

Periodic timers use a monotonic time source for scheduling. The heap priority
and period advancement should use `TimeProvider.GetTimestamp()`,
`Stopwatch.GetTimestamp()`, or an equivalent monotonic timestamp. UTC wall-clock
time is used only for observability fields such as `DueAtUtc` and
`ObservedAtUtc`.

Periodic timers use fixed-rate scheduling without catch-up storms:

- Each registration stores the last scheduled monotonic due timestamp.
- After a due entry is processed, the scheduler advances the next due time to
  the first slot strictly after `now`.
- Missed slots caused by GC pauses, scheduler delay, or `SkipIfPending` are
  counted as skipped; they are not dispatched in a burst.
- The scheduler must never pop a large backlog of historical slots for the same
  timer just to catch up.

`Coalesce` should not be exposed in the first version. If a future use case
needs it, it should be added as a new option with precise "at most one
follow-up tick after the current callback completes" semantics.

## Scheduler Design

The internal scheduler is a framework singleton owned by the game server
runtime. It manages all logical timers through:

```txt
Dictionary<TimerId, TimerRegistration>
PriorityQueue<TimerHeapEntry, long>
```

The heap priority is a monotonic timestamp, not `DateTimeOffset.UtcNow`.

Create:

- Generate a new `TimerId`.
- Validate and serialize the callback descriptor and args.
- Store `TimerRegistration` in the dictionary.
- Push the next due time into the min-heap.
- Wake the scheduler loop.

Destroy:

- Remove the registration from the dictionary.
- Mark the registration generation as destroyed.
- Cancel the registration's tick cancellation source so an in-flight callback
  can observe cancellation.
- Do not scan the heap for removal.
- Stale heap entries are skipped when popped because the dictionary entry is
  missing or the generation no longer matches.
- `DestroyTimerAsync` is idempotent for missing or already-destroyed ids.

Dispatch:

- Wait only until the heap root is due.
- Pop all currently due entries.
- Validate each popped entry against the dictionary and generation.
- Lease valid registrations before starting callback dispatch.
- Dispatch valid ticks through the current hotfix callback resolver.
- Mark a timer as pending before it is placed into the bounded dispatch queue.
- For periodic timers, compute the next future due time after callback
  completion and push a new heap entry only if the registration still exists,
  the generation still matches, and the timer was not destroyed.
- For one-shot timers, remove the scheduling entry when the due entry is leased,
  but retain an in-flight operation record and cancellation source until the
  callback finishes.

Destroy and dispatch race semantics:

- If destroy wins before a due entry is leased, no callback starts.
- A timer is pending while it is leased, queued behind bounded dispatch
  concurrency, or running.
- If dispatch already leased the entry, the callback may start or continue after
  `DestroyTimerAsync` returns.
- `DestroyTimerAsync` does not wait for in-flight callback completion.
- No new due entry for that `TimerId` may be leased after `DestroyTimerAsync`
  returns. A callback already leased before destroy may start or continue after
  destroy returns.
- Destroy cancels the in-flight operation cancellation source for leased,
  queued, and running callbacks.
- A periodic callback that completes after destroy must not reschedule itself.

The scheduler should support bounded dispatch concurrency so a large burst of
simultaneously due timers does not flood the thread pool.

## Feature Lifecycle

`HotfixFeatureContext.Configure` remains a declaration-only method. It must not
create runtime timers because the scanner invokes `Configure` while loading and
validating hotfix assemblies.

Feature-owned timers should be created in explicit hotfix feature runtime hooks:

```csharp
public static async ValueTask StartAsync(HotfixFeatureStartCall call)
{
    var timerId = await LakonaTimer.CreatePeriodicTimerAsync<MatchmakingTimerCallbacks, MatchmakingTickArgs>(
        dueTime: TimeSpan.Zero,
        period: TimeSpan.FromMilliseconds(250),
        methodName: nameof(MatchmakingTimerCallbacks.TickAsync),
        args: new MatchmakingTickArgs("default"));

    call.FeatureState.Set("matchmaking.timer", timerId);
}

public static async ValueTask StopAsync(HotfixFeatureStopCall call)
{
    if (call.FeatureState.TryGet("matchmaking.timer", out TimerId timerId))
    {
        await LakonaTimer.DestroyTimerAsync(timerId);
    }
}
```

These hooks are part of the hotfix feature contract, not the stable
`LakonaGameFeature` host lifecycle. They let replaceable hotfix code react to
feature start and stop without moving business behavior into `Server.App`.

Scanner validation:

- `StartAsync` is optional.
- `StopAsync` is optional.
- If present, each hook must be `public static`.
- `StartAsync` must return `ValueTask` and accept exactly one
  `HotfixFeatureStartCall`.
- `StopAsync` must return `ValueTask` and accept exactly one
  `HotfixFeatureStopCall`.
- Instance hooks, generic hooks, wrong return types, and wrong parameter shapes
  fail hotfix validation.

Runtime behavior:

- `StartAsync` runs when a feature actually starts, not during hotfix scanning.
- Feature start order follows the existing resolved feature order from
  `Lakona:Feature`. If the feature configuration is omitted, that resolved
  order is feature-name order. If the configuration lists features explicitly,
  the configured array order is preserved.
- `StartAsync` hooks run after the new hotfix has been validated and published
  for initial server activation or explicit feature enablement.
- `StopAsync` runs when a feature stops, is disabled, or the server shuts down.
- Stop hooks run in reverse start order.
- If one feature `StartAsync` fails during a start batch, already-started
  feature hooks in that batch are stopped in reverse order, and startup or
  feature enablement fails.
- Hotfix reload does not re-run `StartAsync` for features that remain enabled
  with the same feature name; existing timers continue and call the new callback
  method versions.
- If a hotfix reload removes a previously started feature, renames it, or marks
  it disabled, the runtime treats that feature as stopped: it invokes the
  previous version's `StopAsync` under the previous snapshot lease, destroys
  feature-owned timers through that hook, and then clears that feature's
  `HotfixFeatureState`.
- If a hotfix reload introduces a new enabled feature name, the runtime treats
  it as newly started and invokes the new version's `StartAsync` after publish.
- If a reload changes only the implementation for an existing enabled feature
  name, `StartAsync` and `StopAsync` are not invoked automatically.
- Failed hotfix reload leaves the currently started feature state unchanged.

`HotfixFeatureStartCall` and `HotfixFeatureStopCall` should include:

- feature name
- current hotfix services
- `HotfixFeatureState`
- cancellation token

The framework installs the `LakonaTimer` execution scope while invoking these
hooks, so timer creation and destruction are valid inside them.

`HotfixFeatureState` is framework-owned in-memory state keyed by feature name.
It survives hotfix reload and is cleared when the feature stops. It must not
store hotfix objects. The first implementation only needs stable value support
for timer use, such as `TimerId` by string key; broader object storage should
not be added without a separate design.

This means changing a timer period or arguments in code does not affect an
already-created timer by itself. The business code must explicitly destroy and
recreate the timer when it wants a runtime schedule change.

## Hotfix Reload Hooks

The first version should not add a public `OnReload` or `ReloadedAsync` hook.

There is no concrete use case yet, and a reload hook would introduce difficult
semantics around failure, ordering, rollback, timeout, repeated execution, and
whether it is allowed to create timers. Reload remains a code replacement event:
existing timers, RPC services, lifecycle methods, and actor behavior dispatch
through the latest hotfix view.

If a reload hook is added later, it should be a post-publish best-effort hook
with timeout and idempotency requirements. Failure should be reported, not used
to roll back the published hotfix.

## Migration

Remove the old timer surfaces instead of maintaining two systems:

- `HotfixFeatureContext.ScheduleActorTick`
- `HotfixFeatureContext.ScheduleActiveActorTicks`
- `HotfixActorTickScheduler`
- `ActorContext.RegisterTimer`
- `IActorRuntime.RegisterTimer`

The actor runtime may keep timer helpers only as private implementation
details. Delegate-based timer registration must no longer be a public or
hotfix-reachable API. `LakonaTimer.CreateOnceTimerAsync`,
`LakonaTimer.CreatePeriodicTimerAsync`, and `LakonaTimer.DestroyTimerAsync` are
the only timer surface exposed to users.

Agar matchmaking should migrate from feature-declared actor tick:

```csharp
context.ScheduleActorTick<MatchmakingActor>(
    "default",
    TimeSpan.FromMilliseconds(250),
    TickBacklogPolicy.Coalesce,
    nameof(MatchmakingBehavior.TickAsync));
```

to feature runtime start/stop plus a neutral timer callback that calls the
matchmaking actor through the generated actor reference.

## Performance Validation

The implementation must include both CI-safe tests and manual benchmarks.

CI-safe tests should verify:

- create and destroy many timers without deadlock or leakage
- periodic high-frequency timers obey `SkipIfPending`
- 10,000 timer registrations share one scheduler loop and do not create 10,000
  low-level timers
- hotfix reload keeps existing timers and dispatches the new callback method
- timer creation from an old leased snapshot after a newer reload validates
  against that old scope but future ticks resolve against the latest snapshot
- missing method, wrong signature, missing type, deserialization failure, and
  callback exception are reported and skipped
- once timer failures do not retry
- periodic timer failures do retry on the next period
- reload during an in-flight timer callback keeps the old snapshot alive until
  the callback completes
- destroy before dispatch prevents callback start
- destroy during dispatch cancels the tick token, does not wait, and prevents
  reschedule
- shutdown cancels scheduler waits and active timer tick tokens
- periodic timers do not catch up by dispatching a burst after scheduler delay
- scheduling uses a monotonic clock so wall-clock jumps do not cause catch-up
  bursts or long stalls
- the scheduler accepts an injectable `TimeProvider` or fake monotonic clock for
  deterministic due-time tests
- feature `StartAsync` creates timers under a valid execution scope
- feature `StopAsync` destroys timers
- feature start failure rolls back already-started feature hooks
- hotfix reload starts newly added enabled features, stops removed/disabled
  features, and does not restart unchanged feature names
- `LakonaTimer` calls outside framework-managed execution scope fail clearly
- `Hotfix.Abstractions` is shared with the hotfix load context, so `LakonaTimer`
  facade state is not duplicated across load contexts
- argument DTOs can deserialize against the new hotfix type after reload
- generic callback types, generic callback methods, generic root args, open
  generic args, and static callback classes are rejected
- public API/source-scan tests fail if `ScheduleActorTick`,
  `ScheduleActiveActorTicks`, `ActorContext.RegisterTimer`, or
  `IActorRuntime.RegisterTimer` remain public or hotfix-reachable

Manual benchmarks should measure:

- timer counts: 1,000, 10,000, 50,000
- periods: 16 ms, 50 ms, 250 ms, 1 s
- callback costs: empty callback, actor call, simulated room broadcast
- metrics: p50/p95/p99 dispatch delay, throughput, skipped count, allocation,
  CPU, create latency, destroy latency

Benchmarks should live under a repeatable test or benchmark entry point, for
example a dedicated performance test project or an explicit script under
`scripts/game/ci`. CI smoke tests should use bounded assertions that are stable
on shared machines. Manual benchmarks should print enough environment metadata
to compare runs, including runtime version, OS, CPU count, timer count, period,
callback mode, and build configuration.

The "not 10,000 low-level timers" assertion should be made through an internal
test observer on the scheduler rather than by inferring from timing alone. The
observer can expose scheduler loop count, active registration count, dispatched
tick count, skipped tick count, and heap size for tests.

No deeper optimization decision should be made until these measurements exist.
The min-heap scheduler is still part of the initial redesign because it is the
structural baseline for a large number of logical timers.

## Documentation Updates

The implementation should update current docs under `docs/**` once behavior is
implemented. The durable docs should explain:

- the `LakonaTimer` API
- callback method shape
- hotfix reload behavior
- feature `StartAsync` and `StopAsync`
- Actor mailbox guidance for timer callbacks
- performance test and benchmark entry points

This temporary spec under `docs/superpowers/**` should be removed or folded
into durable docs before the work is considered complete.
