# Timer API Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to execute this plan task-by-task. Use implementation subagents with reasoning effort `medium`. Keep work in `D:\Lakona\.worktrees\timer-api-redesign-spec` unless the user explicitly moves the branch.

**Goal:** Replace the old ActorTick-specific timer APIs with one simple, hotfix-safe timer entry point: `LakonaTimer.CreateOnceTimerAsync`, `LakonaTimer.CreatePeriodicTimerAsync`, and `LakonaTimer.DestroyTimerAsync`. Timers are framework-owned, return an opaque `TimerId`, invoke `public static ValueTask Method(TimerTick<TArgs> tick)` callbacks by `nameof(CallbackType.Method)`, survive hotfix reload by resolving the latest active callback method with the same name, and are backed by one min-heap scheduler.

**Architecture:** `Lakona.Game.Server.Hotfix.Abstractions` owns the public facade, stable timer DTOs, feature lifecycle call DTOs, feature state, and internal execution-scope/backend contracts. `Lakona.Game.Server.Hotfix` owns hotfix scanning, dispatch validation, runtime snapshot leases, publish/rollback behavior, and feature Start/Stop invocation. `Lakona.Game.Server` owns the concrete backend, scheduler, TimeProvider integration, local actor preparation, observer metrics, and host shutdown. Samples migrate to lifecycle-created timers. Old ActorTick and actor-context timer APIs are removed.

**Tech Stack:** C# on `net10.0`, `ValueTask`, `TimeProvider`, `PriorityQueue<TElement,TPriority>`, `System.Text.Json`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, xUnit, PowerShell.

---

## File Structure

Create:

- `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/TimerId.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/TimerTick.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/LakonaTimer.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/ILakonaTimerBackend.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/LakonaTimerExecutionScope.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureStartCall.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureStopCall.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureState.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureLifecycleDeclaration.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Properties/AssemblyInfo.cs`
- `src/Lakona.Game.Server.Hotfix/Runtime/HotfixRuntimeSnapshotLease.cs`
- `src/Lakona.Game.Server.Hotfix/Runtime/HotfixFeatureLifecycleCoordinator.cs`
- `src/Lakona.Game.Server.Hotfix/Runtime/HotfixFeatureLifecycleInvoker.cs`
- `src/Lakona.Game.Server.Hotfix/Runtime/IHotfixRuntimePublicationParticipant.cs`
- `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchRuntimeScope.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerBackend.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerScheduler.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerRegistration.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerDescriptor.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerArgsSerializer.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerCallbackResolver.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerSchedulerObserver.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerOptions.cs`
- `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerServiceCollectionExtensions.cs`
- `src/Lakona.Game.Server/Hotfix/HotfixLocalActorPublicationParticipant.cs`
- `tests/Lakona.Game.Server.Hotfix.Tests/LakonaTimerAbstractionsTests.cs`
- `tests/Lakona.Game.Server.Hotfix.Tests/HotfixRuntimeSnapshotLeaseTests.cs`
- `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureLifecycleTests.cs`
- `tests/Lakona.Game.Server.Tests/LakonaTimerSchedulerTests.cs`
- `tests/Lakona.Game.Server.Tests/LakonaTimerIntegrationTests.cs`
- `tests/Lakona.Game.Server.Tests/LakonaTimerPerformanceTests.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingTimerCallbacks.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingTimerArgs.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeTimerCallbacks.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeTimerArgs.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/FeatureTimerKeys.cs`
- `samples/Game.Unity.Agar/Server/App/Contracts/MatchmakingTickContracts.cs`
- `samples/Game.Unity.Agar/Server/App/Contracts/RoomTickContracts.cs`
- `scripts/game/bench-timers.ps1`

Edit:

- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureDeclaration.cs`
- `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs`
- `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatch.cs`
- `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`
- `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixServiceInvoker.cs`
- `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixFeatureCommandInvoker.cs`
- `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`
- `src/Lakona.Game.Server.Hotfix/HotfixServiceCollectionExtensions.cs`
- `src/Lakona.Game.Server.Hotfix/Loading/HotfixAssemblyLoadContext.cs`
- `src/Lakona.Game.Server.Hotfix/IHotfixRuntimeAccessor.cs`
- `src/Lakona.Game.Server.Hotfix/IHotfixServiceProviderAccessor.cs`
- `src/Lakona.Game.Server.Hotfix/IHotfixManager.cs`
- `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
- `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`
- `src/Lakona.Game.Server/Sessions/GameSessionHotfixLifecycleHandler.cs`
- `src/Lakona.Game.Server/Hotfix/HotfixFeatureMessageHandler.cs`
- `src/Lakona.Game.Server/Actors/ActorContext.cs`
- `src/Lakona.Game.Server/Actors/IActorRuntime.cs`
- `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingFeature.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`
- `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`
- `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`
- `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- `src/Lakona.Tool/Lakona.Tool.csproj`

Delete after replacement:

- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixActorTick.cs`
- `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixActorTickDeclaration.cs`
- `src/Lakona.Game.Server/Hotfix/HotfixActorTickScheduler.cs`
- `src/Lakona.Game.Server/Hotfix/HotfixActorTickHostedService.cs`
- `src/Lakona.Game.Server/Hotfix/HotfixActorTickSchedulerObserver.cs`
- `src/Lakona.Game.Server/Hotfix/HotfixActorTickServiceCollectionExtensions.cs`
- `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerTests.cs`
- `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerPerformanceTests.cs`

## Public API Target

The final public timer surface is:

```csharp
public static class LakonaTimer
{
    public static ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    public static ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    public static ValueTask DestroyTimerAsync(
        TimerId timerId,
        CancellationToken cancellationToken = default);
}

public readonly record struct TimerId;

public sealed class TimerTick<TArgs>
{
    public TimerId TimerId { get; init; }
    public TArgs Args { get; init; }
    public IServiceProvider Services { get; init; }
    public DateTimeOffset DueAtUtc { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
```

Callback methods use this shape:

```csharp
public sealed class MatchmakingTimerCallbacks
{
    public static async ValueTask TickAsync(TimerTick<MatchmakingTickArgs> tick)
    {
        await tick.Services
            .GetRequiredService<MatchmakingActors>()
            .Local(new MatchmakingId(tick.Args.ActorId))
            .RunTickAsync(new MatchmakingTickRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc
            })
            .ConfigureAwait(false);
    }
}
```

## Implementation Steps

Each task below must be executed with this loop:

- [ ] Write only the failing tests named by the task.
- [ ] Run the task's focused command and confirm it fails because the planned type, member, behavior, or assertion is missing.
- [ ] Implement the smallest production change that satisfies those tests.
- [ ] Run the task's focused command again and confirm `Failed: 0` or no `rg` output for source-scan tasks.
- [ ] Commit only the task's files before moving to the next task.

- [ ] Task 1: Add timer facade API tests, then implement the Abstractions surface.
  - Files: `tests/Lakona.Game.Server.Hotfix.Tests/LakonaTimerAbstractionsTests.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/TimerId.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/TimerTick.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/LakonaTimer.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/ILakonaTimerBackend.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Timers/LakonaTimerExecutionScope.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Properties/AssemblyInfo.cs`.
  - Test first: add reflection tests proving the only public create/delete methods are `CreateOnceTimerAsync<TCallback,TArgs>(TimeSpan dueTime, string methodName, TArgs args, CancellationToken)`, `CreatePeriodicTimerAsync<TCallback,TArgs>(TimeSpan dueTime, TimeSpan period, string methodName, TArgs args, CancellationToken)`, and `DestroyTimerAsync(TimerId, CancellationToken)`.
  - Test first: add tests for default-invalid `TimerId`, no public valid-id factory, outside-scope create/destroy throwing `InvalidOperationException`, `dueTime: TimeSpan.Zero` accepted by the test backend, negative `dueTime` rejected, and periodic `period <= TimeSpan.Zero` rejected.
  - Implement: `TimerId` stores a private `Guid`, exposes `IsValid`, equality, and invariant `ToString`; an internal factory is visible only through `InternalsVisibleTo`.
  - Implement: `LakonaTimerExecutionScope` uses `AsyncLocal<LakonaTimerExecutionContext?>`, carries backend plus leased runtime context, has an active flag, and deactivates before clearing in `finally` so captured background `ExecutionContext` cannot use timers after the invocation exits.
  - Verify: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --filter LakonaTimerAbstractionsTests`.
  - Expected first run: fails with missing `LakonaTimer`, `TimerId`, or assertion failures for the unimplemented facade.
  - Expected final run: `Failed: 0`.
  - Commit: `git add src/Lakona.Game.Server.Hotfix.Abstractions tests/Lakona.Game.Server.Hotfix.Tests && git commit -m "Add Lakona timer facade abstractions"`.

- [ ] Task 2: Add runtime snapshot leases and safe retirement.
  - Files: `src/Lakona.Game.Server.Hotfix/IHotfixRuntimeAccessor.cs`, `src/Lakona.Game.Server.Hotfix/Runtime/HotfixRuntimeSnapshotLease.cs`, `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`, `tests/Lakona.Game.Server.Hotfix.Tests/HotfixRuntimeSnapshotLeaseTests.cs`.
  - Test first: add tests proving `AcquireCurrent()` pins service provider and load context until the last lease is disposed, double dispose is harmless, failed reload leaves old snapshot current, and retired snapshots dispose only after active leases drain.
  - Implement: extend `HotfixRuntimeSnapshot` with dispatch table, hotfix service provider, main assembly, source metadata, load context, reference count, `AcquireLease()`, and `Retire()`.
  - Implement: keep `IHotfixRuntimeAccessor.Current` temporarily for compatibility, but make new framework code use `AcquireCurrent()`; mark `Current` `[EditorBrowsable(EditorBrowsableState.Never)]`.
  - Verify: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --filter HotfixRuntimeSnapshotLeaseTests`.
  - Expected first run: fails with missing `AcquireCurrent`, lease type, or disposal assertions.
  - Expected final run: `Failed: 0`.
  - Commit: `git add src/Lakona.Game.Server.Hotfix tests/Lakona.Game.Server.Hotfix.Tests && git commit -m "Add hotfix runtime snapshot leases"`.

- [ ] Task 3: Add ambient dispatch scope without exposing internal timer scope to hotfix code.
  - Files: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchRuntimeScope.cs`, `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatch.cs`, `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixServiceInvoker.cs`, `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixFeatureCommandInvoker.cs`, `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`, `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`, `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`.
  - Test first: add generator tests that generated RPC proxies use `using var lease = _hotfixRuntime.AcquireCurrent(); var snapshot = lease.Snapshot;` before reading `snapshot.Services`, and actor refs still dispatch through `HotfixDispatch`.
  - Test first: add hotfix dispatch tests proving `LakonaTimer` can be created from actor behavior dispatch, RPC service dispatch, feature command dispatch, lifecycle dispatch, feature Start/Stop, and timer callback dispatch.
  - Implement: `HotfixDispatchRuntimeScope` stores the current leased snapshot and timer backend in an `AsyncLocal` owned by framework assemblies.
  - Implement: `HotfixDispatch.Invoke`, `HotfixDispatch.InvokeValueTaskAsync`, service invokers, feature command invokers, lifecycle dispatch, feature lifecycle invoker, and timer callback invoker enter `LakonaTimerExecutionScope` internally.
  - Implement: `HotfixDispatch` resolves against the ambient scoped dispatch table when present; otherwise it uses the externally published current table.
  - Verify: `dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --filter HotfixGeneratorTests` and `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --filter HotfixDispatchTests`.
  - Expected first run: fails because generated proxies still read `_hotfixRuntime.Current` and dispatch has no scoped timer context.
  - Expected final run: `Failed: 0` for both focused commands.
  - Commit: `git add src/Lakona.Game.Server.Hotfix src/Lakona.Game.Server.Hotfix.Generators tests/Lakona.Game.Server.Hotfix.Tests tests/Lakona.Game.Server.Hotfix.Generators.Tests && git commit -m "Install hotfix dispatch timer scope"`.

- [ ] Task 4: Close direct `Current.Services` bypasses.
  - Files: `src/Lakona.Game.Server.Hotfix/IHotfixRuntimeAccessor.cs`, `src/Lakona.Game.Server.Hotfix/IHotfixServiceProviderAccessor.cs`, `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`, `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`, `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`, `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`.
  - Test first: add tests showing `IHotfixServiceProviderAccessor.Current` returns the ambient leased snapshot services inside a hotfix dispatch scope and the volatile current services outside a scope.
  - Implement: update sample hotfix helpers that currently call `IHotfixRuntimeAccessor.Current.Services` to use `IHotfixServiceProviderAccessor.Current`, which is backed by the active dispatch scope when present.
  - Implement: source scan must fail the implementation if production or sample hotfix code still uses `.Current.Services`.
  - Verify: `rg -n "Current\\.Services" src samples tests -g "*.cs"` and keep only test fixtures that intentionally exercise legacy compatibility.
  - Expected first run: reports `Current.Services` in Agar hotfix behavior files.
  - Expected final run: reports only intentional compatibility test fixtures.
  - Commit: `git add src/Lakona.Game.Server.Hotfix src/Lakona.Game.Server.Hotfix.Generators tests/Lakona.Game.Server.Hotfix.Generators.Tests samples/Game.Unity.Agar && git commit -m "Route hotfix services through scoped accessor"`.

- [ ] Task 5: Implement timer descriptor validation and JSON payload handling.
  - Files: `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerBackend.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerDescriptor.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerArgsSerializer.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerCallbackResolver.cs`, `tests/Lakona.Game.Server.Tests/LakonaTimerIntegrationTests.cs`.
  - Test first: create tests for missing callback method, non-static callback, wrong return type, wrong parameter shape, generic callback type, generic callback method, generic root `TArgs`, callback type outside active hotfix assembly, non-serializable args, and args round-trip failure.
  - Implement: validate timer creation against the leased snapshot from the current execution scope, not against whatever snapshot is currently externally published.
  - Implement: accept `dueTime == TimeSpan.Zero`, reject only `dueTime < TimeSpan.Zero`, and reject periodic `period <= TimeSpan.Zero`.
  - Implement: store only reload-safe descriptor data: callback assembly name, callback full name, method name, args assembly name, args full name, serializer id, UTF-8 JSON payload, next due timestamp, period, and generation.
  - Implement: never store hotfix `Type`, `MethodInfo`, delegates, `JsonSerializerOptions`, `JsonTypeInfo`, service instances, or raw hotfix args in long-lived registrations.
  - Verify: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter LakonaTimerIntegrationTests`.
  - Expected first run: fails with missing backend/descriptor/resolver types or validation assertions.
  - Expected final run: `Failed: 0`.
  - Commit: `git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests && git commit -m "Add reload-safe timer descriptors"`.

- [ ] Task 6: Implement the min-heap scheduler with bounded dispatch concurrency.
  - Files: `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerScheduler.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerRegistration.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerSchedulerObserver.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerOptions.cs`, `src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerServiceCollectionExtensions.cs`, `tests/Lakona.Game.Server.Tests/LakonaTimerSchedulerTests.cs`.
  - Test first: add fake `TimeProvider` tests for due-ordering, stale heap entries, idempotent destroy, destroy before lease, destroy while queued, destroy while running, one-shot expiry, periodic reschedule, no catch-up storm, shutdown cancellation, and 10,000 timers sharing one scheduler loop.
  - Implement: use one `Dictionary<TimerId,LakonaTimerRegistration>` and one `PriorityQueue<LakonaTimerHeapEntry,long>`; no per-timer `PeriodicTimer`.
  - Implement: use a bounded dispatch channel plus `LakonaTimerOptions.MaxConcurrentCallbacks` worker count so large simultaneous bursts cannot flood the thread pool.
  - Implement: mark a timer pending before enqueueing; pending covers leased, queued, and running states; `SkipIfPending` reports skipped due slots and does not queue follow-up work.
  - Implement: queue-full behavior reports skipped due work for periodic timers and failed/skipped dispatch for one-shot timers according to their natural lifecycle.
  - Verify: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter LakonaTimerSchedulerTests`.
  - Expected first run: fails with missing scheduler/options types or failing fake-time assertions.
  - Expected final run: `Failed: 0`.
  - Commit: `git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests && git commit -m "Implement min-heap Lakona timer scheduler"`.

- [ ] Task 7: Add feature Start/Stop lifecycle with candidate publication and rollback.
  - Files: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureStartCall.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureStopCall.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureState.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureLifecycleDeclaration.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureDeclaration.cs`, `src/Lakona.Game.Server.Hotfix/Runtime/HotfixFeatureLifecycleCoordinator.cs`, `src/Lakona.Game.Server.Hotfix/Runtime/HotfixFeatureLifecycleInvoker.cs`, `src/Lakona.Game.Server.Hotfix/Runtime/IHotfixRuntimePublicationParticipant.cs`, `src/Lakona.Game.Server/Hotfix/HotfixLocalActorPublicationParticipant.cs`, `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`, `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`, `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`, `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureLifecycleTests.cs`.
  - Test first: scanner rejects instance/generic/wrong-return/wrong-parameter Start/Stop hooks and accepts optional `public static ValueTask StartAsync(HotfixFeatureStartCall call)` / `StopAsync(HotfixFeatureStopCall call)`.
  - Test first: lifecycle tests cover configured order, reverse stop order, same feature name reload not rerunning Start/Stop, removed/renamed/disabled features stopping under the previous snapshot, and no public `OnReload` hook.
  - Test first: Start failure keeps the old runtime externally current, prevents external dispatch from observing the candidate runtime, stops already-started candidate features, destroys timers staged by failed candidate Start hooks, and leaves old feature state intact.
  - Implement: build a candidate runtime snapshot and run new-feature Start under a candidate dispatch scope before external publication; `HotfixDispatch` uses ambient candidate table inside Start.
  - Implement: timers created during candidate Start are staged and activated only when candidate Start succeeds and the runtime is atomically committed; rollback destroys staged timers before candidate retirement.
  - Implement: after Start succeeds, atomically swap external current runtime, external dispatch table, current `HotfixSnapshot`, and feature states, then retire removed previous snapshots when leases drain.
  - Verify: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --filter HotfixFeatureLifecycleTests`.
  - Expected first run: fails with missing Start/Stop call DTOs, scanner validation, or rollback assertions.
  - Expected final run: `Failed: 0`.
  - Commit: `git add src/Lakona.Game.Server.Hotfix.Abstractions src/Lakona.Game.Server.Hotfix src/Lakona.Game.Server tests/Lakona.Game.Server.Hotfix.Tests && git commit -m "Add hotfix feature timer lifecycle"`.

- [ ] Task 8: Remove old ActorTick and actor-context timer APIs.
  - Files: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixActorTick.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixActorTickDeclaration.cs`, `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs`, `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`, `src/Lakona.Game.Server/Hotfix/HotfixActorTickScheduler.cs`, `src/Lakona.Game.Server/Hotfix/HotfixActorTickHostedService.cs`, `src/Lakona.Game.Server/Hotfix/HotfixActorTickSchedulerObserver.cs`, `src/Lakona.Game.Server/Hotfix/HotfixActorTickServiceCollectionExtensions.cs`, `src/Lakona.Game.Server/Actors/ActorContext.cs`, `src/Lakona.Game.Server/Actors/IActorRuntime.cs`, `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`, `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerTests.cs`, `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerPerformanceTests.cs`, `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`.
  - Test first: update `HotfixFeatureContextTests`, `HotfixFeatureScannerTests`, `HotfixDispatchTests`, and `HotfixRendererTests` to assert `ScheduleActorTick`, `ScheduleActiveActorTicks`, `HotfixActorTick`, and public `RegisterTimer` APIs are gone.
  - Implement: remove old declarations, scheduler, hosted service, service registration extension, and public actor timer methods.
  - Implement: keep actor-kernel native timer internals only if still required by the actor implementation and unreachable from hotfix/user APIs.
  - Verify: `rg -n "ScheduleActorTick|ScheduleActiveActorTicks|HotfixActorTick|RegisterTimer<|RegisterTimer\\(" src tests samples -g "*.cs"` returns no public or hotfix-reachable old API references.
  - Expected first run: reports old ActorTick and public actor timer references.
  - Expected final run: no public or hotfix-reachable old API references; internal actor-kernel references are separately inspected before committing.
  - Commit: `git add src tests samples && git commit -m "Remove legacy hotfix actor tick APIs"`.

- [ ] Task 9: Migrate Agar matchmaking and room timers.
  - Files: `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingFeature.cs`, `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`, `samples/Game.Unity.Agar/Server/Hotfix/Features/FeatureTimerKeys.cs`, `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingTimerCallbacks.cs`, `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingTimerArgs.cs`, `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeTimerCallbacks.cs`, `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeTimerArgs.cs`, `samples/Game.Unity.Agar/Server/App/Contracts/MatchmakingActorContracts.cs`, `samples/Game.Unity.Agar/Server/App/Contracts/RoomActorContracts.cs`, `samples/Game.Unity.Agar/Server/App/Contracts/MatchmakingTickContracts.cs`, `samples/Game.Unity.Agar/Server/App/Contracts/RoomTickContracts.cs`, `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`, `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`, `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs`, `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`.
  - Test first: update Agar business tests so `MatchmakingFeature.StartAsync` creates one timer using `nameof(MatchmakingTimerCallbacks.TickAsync)`, stores it in `HotfixFeatureState`, and `StopAsync` destroys it.
  - Test first: add tests that `BattleRuntimeTimerCallbacks.TickAsync` enqueues room ticks per active room without awaiting all room work, so one slow room does not make all rooms skip the next scan.
  - Implement: add `MatchmakingTickRequest` and `RoomTickRequest` stable contracts plus `RunTickAsync` methods on actor contracts.
  - Implement: refactor `MatchmakingBehavior.TickAsync` and `RoomBehavior.TickAsync` into contract methods preserving observed timestamp, interval/delta, matching, simulation, publish, and settlement behavior.
  - Implement: `BattleRuntimeTimerCallbacks.TickAsync` enumerates active `RoomActor` ids and uses `IActorRuntime.TryTell` or an equivalent generated non-blocking dispatch path to enqueue one actor mailbox message per room; observer metrics count accepted, rejected, and skipped room enqueue attempts.
  - Implement: the battle-runtime feature timer guards only the scan callback; each room's actor mailbox preserves per-room serialization and prevents one slow room from blocking all other room ticks.
  - Verify: `dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`.
  - Expected first run: fails because contracts/timer callbacks/lifecycle methods are not yet migrated.
  - Expected final run: `Failed: 0`.
  - Commit: `git add samples/Game.Unity.Agar && git commit -m "Migrate Agar sample to LakonaTimer"`.

- [ ] Task 10: Add reload and runtime failure integration coverage.
  - Files: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixManagerTests.cs`, `tests/Lakona.Game.Server.Tests/LakonaTimerIntegrationTests.cs`, `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`.
  - Test first: periodic timer created by v1 invokes v2 after reload without recreating the timer id.
  - Test first: timer creation from an old in-flight callback after reload validates against that old leased snapshot.
  - Test first: missing callback type, missing method, signature mismatch, argument deserialization failure, and callback exception each report and skip.
  - Test first: one-shot timers do not retry after any runtime resolution/deserialization/callback failure; periodic timers attempt again on the next period.
  - Test first: `LakonaTimer` from `Task.Run` after scope exit throws, and Abstractions is loaded from the default AssemblyLoadContext.
  - Verify: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --filter HotfixManagerTests` and `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter LakonaTimerIntegrationTests`.
  - Expected first run: fails with missing reload/failure observations.
  - Expected final run: `Failed: 0` for both focused commands.
  - Commit: `git add tests/Lakona.Game.Server.Hotfix.Tests tests/Lakona.Game.Server.Tests && git commit -m "Cover timer reload failure semantics"`.

- [ ] Task 11: Add timer performance tests and benchmark runner.
  - Files: `tests/Lakona.Game.Server.Tests/LakonaTimerPerformanceTests.cs`, `scripts/game/bench-timers.ps1`.
  - Test first: smoke benchmark asserts nonzero entered ticks, no unbounded queue growth, bounded worker count respected, and benchmark metadata is printed.
  - Implement: full benchmark mode uses environment variables in `TimerBenchmarkOptions` and covers timer counts `1000`, `10000`, `50000`; periods `16 ms`, `50 ms`, `250 ms`, `1000 ms`; callback costs `empty`, `actor`, `simulated-room-broadcast`.
  - Implement: report p50/p95/p99 dispatch latency, throughput, skipped ticks, callback failures, queue depth, queue full skips, active worker count, allocated bytes per tick, CPU time, create latency, destroy latency, active timer count, heap stale entry count, runtime version, OS, processor count, GC mode, and benchmark options.
  - Verify: `pwsh -NoProfile -File scripts/game/bench-timers.ps1 -Smoke`.
  - Expected first run: fails because `LakonaTimerPerformanceTests` or benchmark script is missing.
  - Expected final run: smoke output includes benchmark metadata and focused tests report `Failed: 0`.
  - Commit: `git add tests/Lakona.Game.Server.Tests scripts/game && git commit -m "Add Lakona timer performance benchmarks"`.

- [ ] Task 12: Update docs, package versions, and final validation.
  - Files: `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`, `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`, `src/Lakona.Game.Server/Lakona.Game.Server.csproj`, `src/Lakona.Tool/Lakona.Tool.csproj`, `src/Lakona.Game.Server.Hotfix.Abstractions/README.md`, `src/Lakona.Game.Server.Hotfix/README.md`, `src/Lakona.Game.Server/README.md`, `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`.
  - Implement: bump `Lakona.Game.Server.Hotfix.Abstractions`, `Lakona.Game.Server.Hotfix`, `Lakona.Game.Server`, and `Lakona.Tool` versions because this changes shippable public behavior.
  - Implement: update docs and templates to use `LakonaTimer.CreatePeriodicTimerAsync<TCallback,TArgs>(dueTime, period, nameof(TCallback.Method), args)` and feature Start/Stop.
  - Validate: run `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj`.
  - Validate: run `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`.
  - Validate: run `dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj`.
  - Validate: run `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`.
  - Validate: run `dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`.
  - Validate: run `pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1`.
  - Validate: run `pwsh -NoProfile -File scripts/game/bench-timers.ps1 -Smoke`.
  - Validate: run `git diff --check`.
  - Expected final run: all validation commands complete successfully and `git diff --check` has no output.
  - Commit: `git add src samples tests scripts docs && git commit -m "Finalize Lakona timer API migration"`.

## Review Checklist

- [ ] Public API has exactly one timer entry point family: `LakonaTimer.CreateOnceTimerAsync`, `LakonaTimer.CreatePeriodicTimerAsync`, and `LakonaTimer.DestroyTimerAsync`.
- [ ] No lambda-based callback API is introduced.
- [ ] No public pause API is introduced.
- [ ] No user-supplied timer id is accepted.
- [ ] No timer owner parameter is exposed.
- [ ] `dueTime == TimeSpan.Zero` is valid, while `dueTime < TimeSpan.Zero` and periodic `period <= TimeSpan.Zero` fail creation.
- [ ] No hotfix `Type`, `MethodInfo`, `JsonSerializerOptions`, `JsonTypeInfo`, or service instance is stored in long-lived timer descriptors.
- [ ] Old ActorTick APIs are removed rather than kept as compatibility wrappers.
- [ ] Hotfix reload keeps existing timer ids alive and resolves callbacks against the latest active same-name method.
- [ ] Failed timer runtime resolution, deserialization, or callback execution reports and skips.
- [ ] Candidate feature Start failure cannot be observed by external actor, RPC, lifecycle, feature command, or timer dispatch.
- [ ] Feature Start failure rolls back new feature state and keeps old feature state intact.
- [ ] Direct `IHotfixRuntimeAccessor.Current.Services` usages are eliminated from production and sample hotfix code.
- [ ] Scheduler dispatch concurrency is bounded and destroy handles leased, queued, and running callbacks.
- [ ] Agar active-room timer migration preserves per-room mailbox enqueue behavior so one slow room does not block all room ticks.
- [ ] Performance tests provide quantitative evidence before any optimization decision.
