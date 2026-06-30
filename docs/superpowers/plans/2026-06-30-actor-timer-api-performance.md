# Actor Timer API And Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make hotfix actor tick declarations require explicit `nameof(...)` callback names and add repeatable timer performance coverage before optimizing the scheduler.

**Architecture:** Keep hotfix tick declarations string-based so the stable scheduler never retains delegates into reloadable assemblies. Add narrow internal scheduler observation hooks only for measurement and diagnostics, then use the existing `Lakona.Game.Server.Tests` friend assembly for performance smoke/full benchmark tests. Update durable docs and package versions in the same implementation because the public hotfix authoring API changes.

**Tech Stack:** C# 13 / .NET 10, xUnit v3, `Lakona.Game.Server.Hotfix.Abstractions`, `Lakona.Game.Server`, Agar sample hotfix code, repository Markdown docs.

---

## Scope Check

This plan intentionally covers two connected implementation streams:

- API and documentation change: remove the implicit `"TickAsync"` default and require explicit method names.
- Measurement change: add bounded timer performance coverage without scheduler optimization.

They belong in one plan because the performance work validates the same actor tick surface and both streams touch the same hotfix tick scheduler/test area. Optimization work is explicitly out of scope.

## File Structure

- Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs`: remove default tick method names.
- Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`: bump package version from `0.2.4` to `0.2.5`.
- Modify `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`: bump package version from `0.3.6` to `0.3.7` so the transitive dependency on `Hotfix.Abstractions` ships.
- Modify `src/Lakona.Tool/Lakona.Tool.csproj`: bump package version from `0.14.8` to `0.14.9` so generated starter dependency versions ship.
- Create `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureContextTests.cs`: API reflection and declaration tests for explicit method names.
- Modify `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`: update feature fixture schedule calls to pass `nameof(...)`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingFeature.cs`: pass `nameof(MatchmakingBehavior.TickAsync)`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`: pass `nameof(RoomBehavior.TickAsync)`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixBoundaryTests.cs`: assert Agar source uses explicit `nameof(...)`.
- Modify durable docs:
  - `docs/actor.md`
  - `docs/configuration.md`
  - `docs/hotfix/architecture.md`
  - `docs/hotfix/actor-behavior.md`
- Modify `src/Lakona.Game.Server/Hotfix/HotfixActorTickScheduler.cs`: add internal observation callbacks with no public API.
- Create `src/Lakona.Game.Server/Hotfix/HotfixActorTickSchedulerObserver.cs`: internal observer interface and observation DTOs.
- Modify `src/Lakona.Game.Server/Lakona.Game.Server.csproj`: bump package version from `0.8.19` to `0.8.20` because internal scheduler diagnostics change shippable package content.
- Create `tests/Lakona.Game.Server.Tests/HotfixDispatchCollection.cs`: xUnit collection for tests that mutate the process-global `HotfixDispatch` table.
- Create `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerPerformanceTests.cs`: xUnit smoke/full benchmark tests using the real runtime.
- Modify `docs/superpowers/specs/2026-06-30-actor-timer-design.md` and this plan only at the final cleanup task after durable docs contain the rules.

### Task 1: Lock The Public Hotfix Feature API Contract

**Files:**
- Create: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureContextTests.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`

- [ ] **Step 1: Add failing API contract tests**

Create `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureContextTests.cs`:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureContextTests
{
    [Fact]
    public void ScheduleActorTick_requires_explicit_method_name_parameter()
    {
        var method = typeof(HotfixFeatureContext).GetMethod(nameof(HotfixFeatureContext.ScheduleActorTick))!;

        var methodName = Assert.Single(method.GetParameters(), parameter => parameter.Name == "methodName");
        Assert.False(methodName.HasDefaultValue);
    }

    [Fact]
    public void ScheduleActiveActorTicks_requires_explicit_method_name_parameter()
    {
        var method = typeof(HotfixFeatureContext).GetMethod(nameof(HotfixFeatureContext.ScheduleActiveActorTicks))!;

        var methodName = Assert.Single(method.GetParameters(), parameter => parameter.Name == "methodName");
        Assert.False(methodName.HasDefaultValue);
    }

    [Fact]
    public void ScheduleActorTick_records_explicit_method_name()
    {
        var context = new HotfixFeatureContext();

        context.ScheduleActorTick<ContextTickActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce,
            nameof(ContextTickBehavior.TickAsync));

        var tick = Assert.Single(context.ActorTicks);
        Assert.Equal(HotfixActorTickMode.FixedActor, tick.Mode);
        Assert.Equal(typeof(ContextTickActor), tick.ActorType);
        Assert.Equal("default", tick.ActorId);
        Assert.Equal(nameof(ContextTickBehavior.TickAsync), tick.MethodName);
        Assert.Equal(TimeSpan.FromMilliseconds(250), tick.Interval);
        Assert.Equal(TickBacklogPolicy.Coalesce, tick.BacklogPolicy);
    }

    [Fact]
    public void ScheduleActiveActorTicks_records_explicit_method_name()
    {
        var context = new HotfixFeatureContext();

        context.ScheduleActiveActorTicks<ContextTickActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending,
            nameof(ContextTickBehavior.TickAsync));

        var tick = Assert.Single(context.ActorTicks);
        Assert.Equal(HotfixActorTickMode.ActiveActors, tick.Mode);
        Assert.Equal(typeof(ContextTickActor), tick.ActorType);
        Assert.Equal("", tick.ActorId);
        Assert.Equal(nameof(ContextTickBehavior.TickAsync), tick.MethodName);
        Assert.Equal(TimeSpan.FromMilliseconds(50), tick.Interval);
        Assert.Equal(TickBacklogPolicy.SkipIfPending, tick.BacklogPolicy);
    }

    [Fact]
    public void ScheduleActorTick_rejects_blank_method_name()
    {
        var context = new HotfixFeatureContext();

        Assert.Throws<ArgumentException>(() =>
            context.ScheduleActorTick<ContextTickActor>(
                "default",
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce,
                ""));
    }

    [Fact]
    public void ScheduleActiveActorTicks_rejects_blank_method_name()
    {
        var context = new HotfixFeatureContext();

        Assert.Throws<ArgumentException>(() =>
            context.ScheduleActiveActorTicks<ContextTickActor>(
                TimeSpan.FromMilliseconds(50),
                TickBacklogPolicy.SkipIfPending,
                ""));
    }

    private sealed class ContextTickActor
    {
    }

    private static class ContextTickBehavior
    {
        public static ValueTask TickAsync(ContextTickActor actor, HotfixActorTick tick)
        {
            _ = actor;
            _ = tick;
            return default;
        }
    }
}
```

- [ ] **Step 2: Run the new API tests and verify the default-value assertions fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter HotfixFeatureContextTests
```

Expected: the two `requires_explicit_method_name_parameter` tests fail because `methodName` still has default value `"TickAsync"`.

- [ ] **Step 3: Remove the default values from the API**

Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs` so the two methods become:

```csharp
public void ScheduleActorTick<TActor>(
    string actorId,
    TimeSpan interval,
    TickBacklogPolicy backlogPolicy,
    string methodName)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
    AddTick(typeof(TActor), HotfixActorTickMode.FixedActor, actorId, interval, backlogPolicy, methodName);
}

public void ScheduleActiveActorTicks<TActor>(
    TimeSpan interval,
    TickBacklogPolicy backlogPolicy,
    string methodName)
{
    AddTick(typeof(TActor), HotfixActorTickMode.ActiveActors, "", interval, backlogPolicy, methodName);
}
```

- [ ] **Step 4: Update the hotfix scanner test fixture to compile with explicit names**

Modify the `BattleRuntimeFeature.Configure` fixture inside `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`:

```csharp
context.ScheduleActorTick<MatchmakingActor>(
    "default",
    TimeSpan.FromMilliseconds(250),
    TickBacklogPolicy.Coalesce,
    nameof(TickBehavior.TickAsync));
context.ScheduleActiveActorTicks<RoomActor>(
    TimeSpan.FromMilliseconds(50),
    TickBacklogPolicy.SkipIfPending,
    nameof(TickBehavior.TickAsync));
```

Add this helper in the same test class near the private actor fixture types:

```csharp
private static class TickBehavior
{
    public static ValueTask TickAsync(MatchmakingActor actor, HotfixActorTick tick)
    {
        _ = actor;
        _ = tick;
        return default;
    }

    public static ValueTask TickAsync(RoomActor actor, HotfixActorTick tick)
    {
        _ = actor;
        _ = tick;
        return default;
    }
}
```

Leave the existing assertions for `fixedTick.MethodName` and `activeTick.MethodName` as `"TickAsync"`; the scanner still stores the method name string.

- [ ] **Step 5: Add negative dispatch validation tests for tick method declarations**

Modify `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs` with these tests near the feature command validation tests:

```csharp
[Fact]
public void FeatureTickValidationRejectsMissingTickMethod()
{
    var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());

    var exception = Assert.Throws<HotfixMethodNotLoadedException>(() =>
        table.ValidateFeatureTickMethods([
            CreateTickFeatureDeclaration(typeof(TickDispatchActor), "MissingTickAsync")
        ]));

    Assert.Contains("MissingTickAsync", exception.Message);
    Assert.Contains("is not loaded", exception.Message);
}

[Fact]
public void FeatureTickValidationRejectsMalformedTickMethod()
{
    var method = typeof(MalformedTickBehavior).GetMethod(nameof(MalformedTickBehavior.TickAsync))!;
    var binding = new HotfixMethodBinding(
        HotfixDispatch.CreateKey(
            typeof(TickDispatchActor),
            nameof(MalformedTickBehavior.TickAsync),
            typeof(ValueTask),
            [typeof(HotfixActorTick)]),
        method,
        typeof(TickDispatchActor),
        typeof(ValueTask),
        [typeof(HotfixActorTick)]);
    var table = new HotfixDispatchTable(1, [binding]);

    var exception = Assert.Throws<InvalidOperationException>(() =>
        table.ValidateFeatureTickMethods([
            CreateTickFeatureDeclaration(typeof(TickDispatchActor), nameof(MalformedTickBehavior.TickAsync))
        ]));

    Assert.Contains("Hotfix tick method", exception.Message);
    Assert.Contains("HotfixActorTick", exception.Message);
}
```

Add these helpers and fixtures near the other `HotfixDispatchTests` helper types:

```csharp
private static HotfixFeatureDeclaration CreateTickFeatureDeclaration(Type actorType, string methodName)
{
    return new HotfixFeatureDeclaration(
        "tick-feature",
        typeof(DispatchFeature),
        Discoverable: true,
        new Dictionary<string, string>(),
        [],
        [new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            actorType,
            "default",
            methodName,
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce)],
        [],
        []);
}

public sealed class TickDispatchActor
{
}

public static class MalformedTickBehavior
{
    public static ValueTask TickAsync(TickDispatchActor actor)
    {
        _ = actor;
        return default;
    }
}
```

- [ ] **Step 6: Run hotfix abstraction tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
```

Expected: all tests in `Lakona.Game.Server.Hotfix.Tests` pass.

- [ ] **Step 7: Commit the API contract change**

Run:

```powershell
git add src\Lakona.Game.Server.Hotfix.Abstractions\Features\HotfixFeatureContext.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixFeatureContextTests.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixFeatureScannerTests.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixDispatchTests.cs
git commit -m "Require explicit hotfix actor tick method names"
```

### Task 2: Update Agar Tick Declarations And Source Tests

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingFeature.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixBoundaryTests.cs`

- [ ] **Step 1: Update MatchmakingFeature to use `nameof`**

Modify `samples/Game.Unity.Agar/Server/Hotfix/Features/MatchmakingFeature.cs`:

```csharp
using Agar.Sample.State.Matchmaking;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.Hotfix.State.Matchmaking;

namespace Server.Hotfix.Features;

[HotfixFeature("matchmaking")]
public sealed class MatchmakingFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce,
            nameof(MatchmakingBehavior.TickAsync));
    }
}
```

- [ ] **Step 2: Update BattleRuntimeFeature to use `nameof`**

Modify the tick declaration in `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`:

```csharp
context.ScheduleActiveActorTicks<RoomActor>(
    TimeSpan.FromMilliseconds(50),
    TickBacklogPolicy.SkipIfPending,
    nameof(RoomBehavior.TickAsync));
```

The file already imports `Server.Hotfix.State.Rooms`, so no new using is needed.

- [ ] **Step 3: Strengthen Agar boundary tests**

Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixBoundaryTests.cs` in `Agar_user_features_are_hotfix_descriptors`:

```csharp
Assert.Contains("ScheduleActorTick<MatchmakingActor>", hotfixText, StringComparison.Ordinal);
Assert.Contains("nameof(MatchmakingBehavior.TickAsync)", hotfixText, StringComparison.Ordinal);
Assert.Contains("ScheduleActiveActorTicks<RoomActor>", hotfixText, StringComparison.Ordinal);
Assert.Contains("nameof(RoomBehavior.TickAsync)", hotfixText, StringComparison.Ordinal);
```

Modify `Hotfix_matchmaking_feature_owns_default_queue_actor_ticks`:

```csharp
Assert.Contains("EnsureLocalActor<MatchmakingActor>", matchmakingFeature, StringComparison.Ordinal);
Assert.Contains("ScheduleActorTick<MatchmakingActor>", matchmakingFeature, StringComparison.Ordinal);
Assert.Contains("nameof(MatchmakingBehavior.TickAsync)", matchmakingFeature, StringComparison.Ordinal);
Assert.Contains("\"default\"", matchmakingFeature, StringComparison.Ordinal);
Assert.DoesNotContain("ScheduleActorTick<MatchmakingActor>", battleRuntimeFeature, StringComparison.Ordinal);
Assert.Contains("nameof(RoomBehavior.TickAsync)", battleRuntimeFeature, StringComparison.Ordinal);
```

- [ ] **Step 4: Run Agar business logic tests**

Run:

```powershell
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --no-restore
```

Expected: all Agar business logic tests pass.

- [ ] **Step 5: Commit Agar source updates**

Run:

```powershell
git add samples\Game.Unity.Agar\Server\Hotfix\Features\MatchmakingFeature.cs samples\Game.Unity.Agar\Server\Hotfix\Features\BattleRuntimeFeature.cs samples\Game.Unity.Agar\tests\BusinessLogic.Tests\AgarHotfixBoundaryTests.cs
git commit -m "Use explicit actor tick methods in Agar features"
```

### Task 3: Update Durable Documentation

**Files:**
- Modify: `docs/actor.md`
- Modify: `docs/configuration.md`
- Modify: `docs/hotfix/architecture.md`
- Modify: `docs/hotfix/actor-behavior.md`

- [ ] **Step 1: Update actor tick examples**

In each durable doc tick example, change schedule calls to this shape:

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

Apply this in:

- `docs/actor.md`
- `docs/configuration.md`
- `docs/hotfix/architecture.md`
- `docs/hotfix/actor-behavior.md`

- [ ] **Step 2: Add the reason for `nameof(...)` to `docs/actor.md`**

In `docs/actor.md`, after the paragraph ending with "skipped-tick diagnostics, slow-tick diagnostics, and shutdown.", add:

```markdown
The method name is explicit on purpose. Use `nameof(...)` so the call site shows
which behavior method will run and normal refactoring tools keep the declaration
in sync. The scheduler stores the method name rather than a delegate because a
delegate could keep an old reloadable hotfix assembly generation alive after
reload.
```

- [ ] **Step 3: Add the local benchmark command to `docs/actor.md`**

In `docs/actor.md`, before `## Analyzer Boundary`, add:

````markdown
### Actor Tick Performance Checks

Actor tick performance coverage lives in `Lakona.Game.Server.Tests`. CI runs a
short smoke path. Maintainers can run the larger local benchmark with:

```powershell
$env:LAKONA_TIMER_BENCHMARK_FULL='1'
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter HotfixActorTickSchedulerPerformanceTests --logger "console;verbosity=detailed"
Remove-Item Env:\LAKONA_TIMER_BENCHMARK_FULL
```

Treat benchmark output as evidence for future scheduler optimization. Do not
optimize actor tick internals without before/after numbers from this path or an
equivalent focused benchmark.
````

- [ ] **Step 4: Add a docs scan test to prevent accidental old examples**

Create or modify a source-scan test in `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureContextTests.cs`:

```csharp
[Fact]
public void Durable_docs_do_not_show_actor_tick_schedule_calls_without_method_names()
{
    var repositoryRoot = FindRepositoryRoot();
    var docs = new[]
    {
        Path.Combine(repositoryRoot, "docs", "actor.md"),
        Path.Combine(repositoryRoot, "docs", "configuration.md"),
        Path.Combine(repositoryRoot, "docs", "hotfix", "architecture.md"),
        Path.Combine(repositoryRoot, "docs", "hotfix", "actor-behavior.md")
    };

    foreach (var path in docs)
    {
        var text = File.ReadAllText(path);
        Assert.DoesNotContain(
            "TickBacklogPolicy.Coalesce);",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TickBacklogPolicy.SkipIfPending);",
            text,
            StringComparison.Ordinal);
    }
}

private static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Game.Server.Hotfix.Abstractions"))
            && Directory.Exists(Path.Combine(directory.FullName, "docs")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
}
```

Place `FindRepositoryRoot` once in `HotfixFeatureContextTests`, before the nested helper fixture types.

- [ ] **Step 5: Run documentation-related tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter HotfixFeatureContextTests
```

Expected: all `HotfixFeatureContextTests` pass.

- [ ] **Step 6: Commit durable docs**

Run:

```powershell
git add docs\actor.md docs\configuration.md docs\hotfix\architecture.md docs\hotfix\actor-behavior.md tests\Lakona.Game.Server.Hotfix.Tests\HotfixFeatureContextTests.cs
git commit -m "Document explicit actor tick method declarations"
```

### Task 4: Bump Package Versions And Record BuildTag Decision

**Files:**
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`
- Modify: `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`
- Inspect: `samples/Game.Unity.Agar/Server/App/Server.App.csproj`
- Inspect: `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- Inspect: `src/Lakona.Tool/Planning/DependencyPlanner.cs`

- [ ] **Step 1: Bump Hotfix.Abstractions package version**

Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`:

```xml
<Version>0.2.5</Version>
```

- [ ] **Step 2: Bump the transitive hotfix runtime package version**

Modify `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`:

```xml
<Version>0.3.7</Version>
```

This package must be republished even though its source code does not change. Otherwise downstream packages can keep resolving the already-published `Lakona.Game.Server.Hotfix` package whose dependency metadata points at the old `Lakona.Game.Server.Hotfix.Abstractions` version.

- [ ] **Step 3: Bump the tool package version for generated dependency versions**

Modify `src/Lakona.Tool/Lakona.Tool.csproj`:

```xml
<Version>0.14.9</Version>
```

`Lakona.Tool` generates starter package references from the current project versions at build time. Bump the tool package so the updated `Lakona.Game.Server`, `Lakona.Game.Server.Hotfix`, and `Lakona.Game.Server.Hotfix.Abstractions` versions can ship in the next tool package.

- [ ] **Step 4: Confirm `Lakona.Tool` does not need a planner source edit for Hotfix.Abstractions**

Inspect `src/Lakona.Tool/Planning/DependencyPlanner.cs`. `ServerApp` references `Lakona.Game.Server`, `Lakona.Game.Server.Hotfix`, and `Lakona.Game.Server.Hotfix.Generators`; it does not directly emit `Lakona.Game.Server.Hotfix.Abstractions`. The `ServerHotfix` project references `Server.App` plus `Lakona.Game.Server.Hotfix.Generators`. Therefore the package version bump is consumed transitively, and no `DependencyPlanner` code change is required for this API change.

Inspect `src/Lakona.Tool/Lakona.Tool.csproj`. It already reads `Lakona.Game.Server.Hotfix.Abstractions.csproj` into `LakonaGameServerHotfixAbstractionsPackageVersion`, but `PackageCatalog` does not expose that value. Leave that wiring unchanged in this implementation because generated starters do not directly emit a package reference to `Lakona.Game.Server.Hotfix.Abstractions`.

- [ ] **Step 5: Verify the Agar BuildTag decision**

Inspect `samples/Game.Unity.Agar/Server/App/Server.App.csproj` and confirm it still uses the development assembly metadata value `<_Parameter2>dev</_Parameter2>`, not a versioned `BuildTag.props`. Do not edit that sample file only to replace `dev`.

Inspect generated starter code and confirm generated hotfix features do not emit actor tick schedule declarations. Leave generated starter `BuildTag.props` at `20260629.001` for this implementation.

- [ ] **Step 6: Build the tool project enough to verify generated package-version code still compiles**

Run:

```powershell
dotnet build src\Lakona.Tool\Lakona.Tool.csproj --no-restore
```

Expected: build passes and `ToolPackageVersions.g.cs` is generated under `obj`, not committed. The generated constants include `LakonaGameServerHotfix = "0.3.7"` and `LakonaGameServerHotfixAbstractions = "0.2.5"`. After Task 5, the same generated file will also read `LakonaGameServer = "0.8.20"` because `Lakona.Game.Server.csproj` is bumped there.

- [ ] **Step 7: Commit version decision**

Run:

```powershell
git add src\Lakona.Game.Server.Hotfix.Abstractions\Lakona.Game.Server.Hotfix.Abstractions.csproj src\Lakona.Game.Server.Hotfix\Lakona.Game.Server.Hotfix.csproj src\Lakona.Tool\Lakona.Tool.csproj
git commit -m "Bump hotfix abstractions for explicit tick methods"
```

### Task 5: Add Internal Scheduler Observation Hooks

**Files:**
- Create: `src/Lakona.Game.Server/Hotfix/HotfixActorTickSchedulerObserver.cs`
- Modify: `src/Lakona.Game.Server/Hotfix/HotfixActorTickScheduler.cs`
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Create: `tests/Lakona.Game.Server.Tests/HotfixDispatchCollection.cs`
- Modify: `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerTests.cs`

- [ ] **Step 1: Add an internal observer contract**

Create `src/Lakona.Game.Server/Hotfix/HotfixActorTickSchedulerObserver.cs`:

```csharp
using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

internal interface IHotfixActorTickSchedulerObserver
{
    void OnDispatchAccepted(HotfixActorTickDispatchObservation observation);

    void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result);

    void OnDispatchSkipped(HotfixActorTickDispatchObservation observation);

    void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation);

    void OnTickEntered(HotfixActorTickEntryObservation observation);
}

internal readonly record struct HotfixActorTickDispatchObservation(
    string SourceKey,
    Type ActorType,
    ActorId ActorId,
    string MethodName,
    TimeSpan Interval,
    TickBacklogPolicy BacklogPolicy,
    long QueuedTimestamp);

internal readonly record struct HotfixActorTickEntryObservation(
    string SourceKey,
    Type ActorType,
    ActorId ActorId,
    string MethodName,
    TimeSpan Interval,
    TickBacklogPolicy BacklogPolicy,
    long Sequence,
    long QueuedTimestamp,
    long EnteredTimestamp)
{
    public TimeSpan QueueLatency => Stopwatch.GetElapsedTime(QueuedTimestamp, EnteredTimestamp);
}

internal sealed class NullHotfixActorTickSchedulerObserver : IHotfixActorTickSchedulerObserver
{
    public static readonly NullHotfixActorTickSchedulerObserver Instance = new();

    private NullHotfixActorTickSchedulerObserver()
    {
    }

    public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
    {
    }

    public void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result)
    {
    }

    public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
    {
    }

    public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
    {
    }

    public void OnTickEntered(HotfixActorTickEntryObservation observation)
    {
    }
}
```

- [ ] **Step 2: Wire the observer into the scheduler without changing public behavior**

Modify the scheduler declaration in `src/Lakona.Game.Server/Hotfix/HotfixActorTickScheduler.cs`:

```csharp
using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorTickScheduler(
    IActorRuntime actors,
    ILogger<HotfixActorTickScheduler> logger,
    IHotfixActorTickSchedulerObserver? observer = null) : IAsyncDisposable
{
    private readonly IHotfixActorTickSchedulerObserver _observer =
        observer ?? NullHotfixActorTickSchedulerObserver.Instance;
```

- [ ] **Step 3: Emit skipped/coalesced observations in `Dispatch`**

In `Dispatch`, create an observation before the lock:

```csharp
var key = new PendingKey(source.Key, actorId);
var observation = CreateDispatchObservation(source, actorId);
PendingState? pending = null;
```

Keep observer callbacks outside `_sync`:

```csharp
var skipped = false;
var coalesced = false;
lock (_sync)
{
    if (_pending.TryGetValue(key, out pending!))
    {
        if (source.BacklogPolicy == TickBacklogPolicy.Coalesce)
        {
            pending.Coalesced = true;
            coalesced = true;
        }
        else
        {
            skipped = true;
        }
    }
    else
    {
        pending = new PendingState();
        _pending.Add(key, pending);
    }
}

if (coalesced)
{
    _observer.OnDispatchCoalesced(observation);
    return;
}

if (skipped)
{
    _observer.OnDispatchSkipped(observation);
    logger.LogDebug(
        "Skipping hotfix actor tick {TickSource} for actor {ActorId}; previous tick is pending.",
        source.Key,
        actorId.Value);
    return;
}
```

Pass `observation` into `DispatchPending`:

```csharp
var pendingState = pending ?? throw new InvalidOperationException("Pending state was not created.");
DispatchPending(source, actorId, key, pendingState, observation);
```

- [ ] **Step 4: Emit accepted/rejected/entry observations in `DispatchPending`**

Change the signature:

```csharp
private void DispatchPending(
    TickSource source,
    ActorId actorId,
    PendingKey key,
    PendingState pending,
    HotfixActorTickDispatchObservation observation)
```

Inside the actor callback, record entry before invoking the hotfix method:

```csharp
var sequence = Interlocked.Increment(ref pending.Sequence);
var tick = new HotfixActorTick
{
    ObservedAtUtc = DateTime.UtcNow,
    Interval = source.Interval,
    Sequence = sequence,
    DispatchTableVersion = table.Version
};

_observer.OnTickEntered(new HotfixActorTickEntryObservation(
    source.Key,
    source.ActorType,
    actorId,
    source.MethodName,
    source.Interval,
    source.BacklogPolicy,
    sequence,
    observation.QueuedTimestamp,
    Stopwatch.GetTimestamp()));
```

After `TryTell`, report accept/reject:

```csharp
if (result == ActorTellResult.Accepted)
{
    _observer.OnDispatchAccepted(observation);
    return;
}

_observer.OnDispatchRejected(observation, result);
```

- [ ] **Step 5: Preserve coalesced follow-up observation**

In `CompletePending`, update the follow-up call:

```csharp
if (dispatchFollowUp)
{
    DispatchPending(source, actorId, key, pending, CreateDispatchObservation(source, actorId));
}
```

Add this helper method near `DispatchPending`:

```csharp
private static HotfixActorTickDispatchObservation CreateDispatchObservation(TickSource source, ActorId actorId)
{
    return new HotfixActorTickDispatchObservation(
        source.Key,
        source.ActorType,
        actorId,
        source.MethodName,
        source.Interval,
        source.BacklogPolicy,
        Stopwatch.GetTimestamp());
}
```

- [ ] **Step 6: Add a focused observer test**

Create `tests/Lakona.Game.Server.Tests/HotfixDispatchCollection.cs`:

```csharp
using Xunit;

namespace Lakona.Game.Server.Tests;

internal static class HotfixDispatchCollectionNames
{
    public const string GlobalState = "Hotfix dispatch global state";
}

[CollectionDefinition(HotfixDispatchCollectionNames.GlobalState, DisableParallelization = true)]
public sealed class HotfixDispatchCollection;
```

Modify the class declaration in `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerTests.cs` so existing scheduler tests join the same collection:

```csharp
[Collection(HotfixDispatchCollectionNames.GlobalState)]
public sealed class HotfixActorTickSchedulerTests : IDisposable
```

Then add a new test:

```csharp
[Fact]
public async Task Observer_records_accepted_entered_skipped_and_coalesced_ticks()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var runtime = new RecordingActorRuntime();
    runtime.BlockedActorId = ActorId.From("fixed");
    var observer = new RecordingTickObserver();
    await using var scheduler = new HotfixActorTickScheduler(
        runtime,
        NullLogger<HotfixActorTickScheduler>.Instance,
        observer);

    HotfixDispatch.Replace(CreateTickTable(1));
    scheduler.Apply(CreateSnapshot(
        new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(TickActor),
            "fixed",
            nameof(TickHotfix.TickAsync),
            TimeSpan.FromMilliseconds(10),
            TickBacklogPolicy.Coalesce)));

    await runtime.BlockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
    await Task.Delay(60, cancellationToken);
    runtime.ReleaseBlocked();
    await TickHotfix.WaitForCountAsync(2, cancellationToken);

    Assert.True(observer.AcceptedCount >= 1);
    Assert.True(observer.EnteredCount >= 2);
    Assert.True(observer.CoalescedCount >= 1);
    Assert.Equal(0, observer.RejectedCount);
    Assert.All(observer.EntryLatencies, latency => Assert.True(latency >= TimeSpan.Zero));
}
```

Add this helper class near the other test fixtures:

```csharp
private sealed class RecordingTickObserver : IHotfixActorTickSchedulerObserver
{
    private readonly object _sync = new();
    private readonly List<TimeSpan> _entryLatencies = [];

    public int AcceptedCount { get; private set; }

    public int RejectedCount { get; private set; }

    public int SkippedCount { get; private set; }

    public int CoalescedCount { get; private set; }

    public int EnteredCount { get; private set; }

    public IReadOnlyList<TimeSpan> EntryLatencies
    {
        get
        {
            lock (_sync)
            {
                return _entryLatencies.ToArray();
            }
        }
    }

    public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
    {
        _ = observation;
        lock (_sync)
        {
            AcceptedCount++;
        }
    }

    public void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result)
    {
        _ = observation;
        _ = result;
        lock (_sync)
        {
            RejectedCount++;
        }
    }

    public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
    {
        _ = observation;
        lock (_sync)
        {
            SkippedCount++;
        }
    }

    public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
    {
        _ = observation;
        lock (_sync)
        {
            CoalescedCount++;
        }
    }

    public void OnTickEntered(HotfixActorTickEntryObservation observation)
    {
        lock (_sync)
        {
            EnteredCount++;
            _entryLatencies.Add(observation.QueueLatency);
        }
    }
}
```

- [ ] **Step 7: Bump `Lakona.Game.Server` package version**

Modify `src/Lakona.Game.Server/Lakona.Game.Server.csproj`:

```xml
<Version>0.8.20</Version>
```

- [ ] **Step 8: Run scheduler tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter HotfixActorTickSchedulerTests
```

Expected: all `HotfixActorTickSchedulerTests` pass.

- [ ] **Step 9: Commit scheduler observation hooks**

Run:

```powershell
git add src\Lakona.Game.Server\Hotfix\HotfixActorTickScheduler.cs src\Lakona.Game.Server\Hotfix\HotfixActorTickSchedulerObserver.cs src\Lakona.Game.Server\Lakona.Game.Server.csproj tests\Lakona.Game.Server.Tests\HotfixActorTickSchedulerTests.cs tests\Lakona.Game.Server.Tests\HotfixDispatchCollection.cs
git commit -m "Add internal actor tick scheduler observations"
```

### Task 6: Add Timer Performance Smoke And Full Benchmark

**Files:**
- Create: `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerPerformanceTests.cs`
- Modify: `docs/actor.md`

- [ ] **Step 1: Add the performance test file**

Create `tests/Lakona.Game.Server.Tests/HotfixActorTickSchedulerPerformanceTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

[Collection(HotfixDispatchCollectionNames.GlobalState)]
public sealed class HotfixActorTickSchedulerPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public HotfixActorTickSchedulerPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Active_actor_ticks_report_smoke_or_full_benchmark()
    {
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        foreach (var actorCount in options.ActorCounts)
        {
            for (var iteration = 1; iteration <= options.Iterations; iteration++)
            {
                var result = await RunActiveActorScenarioAsync(
                    actorCount,
                    TimeSpan.FromMilliseconds(50),
                    TickBacklogPolicy.SkipIfPending,
                    options,
                    iteration,
                    TestContext.Current.CancellationToken).ConfigureAwait(false);

                WriteResult(result);
                Assert.True(result.EnteredTicks > 0);
                Assert.Equal(0, result.RejectedTicks);
            }
        }
    }

    [Fact]
    public async Task Fixed_singleton_ticks_report_smoke_or_full_benchmark()
    {
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        for (var iteration = 1; iteration <= options.Iterations; iteration++)
        {
            var result = await RunFixedActorScenarioAsync(
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce,
                options,
                iteration,
                TestContext.Current.CancellationToken).ConfigureAwait(false);

            WriteResult(result);
            Assert.True(result.EnteredTicks > 0);
            Assert.Equal(0, result.RejectedTicks);
        }
    }

    [Fact]
    public async Task Missing_fixed_actor_ticks_report_rejections_without_creating_actor()
    {
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        var result = await RunMissingActorScenarioAsync(
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.SkipIfPending,
            options,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        WriteResult(result);
        Assert.Equal(0, result.EnteredTicks);
        Assert.True(result.RejectedTicks > 0);
    }

    [Fact]
    public async Task Busy_fixed_actor_ticks_report_backlog_policy_behavior()
    {
        var options = TimerBenchmarkOptions.FromEnvironment();
        WriteRuntimeMetadata(options);

        foreach (var policy in new[] { TickBacklogPolicy.SkipIfPending, TickBacklogPolicy.Coalesce })
        {
            var result = await RunBusyFixedActorScenarioAsync(
                policy,
                options,
                TestContext.Current.CancellationToken).ConfigureAwait(false);

            WriteResult(result);
            Assert.True(result.EnteredTicks > 0);
            Assert.True(
                policy == TickBacklogPolicy.Coalesce
                    ? result.CoalescedTicks > 0
                    : result.SkippedTicks > 0);
        }
    }

    public void Dispose()
    {
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
    }

    private static ServiceProvider CreateProvider()
    {
        return new ServiceCollection()
            .AddLakonaGameServerActors(options =>
            {
                options.MailboxCapacity = 8192;
            })
            .BuildServiceProvider();
    }

    private async Task<TimerBenchmarkResult> RunActiveActorScenarioAsync(
        int actorCount,
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        int iteration,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var observer = new BenchmarkTickObserver();
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BenchmarkRoomActor), nameof(BenchmarkRoomBehavior.TickAsync)));

        for (var i = 0; i < actorCount; i++)
        {
            var created = await lifecycle.CreateLocalAsync<BenchmarkRoomActor>(
                ActorId.From($"bench-room/{i:D5}"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            Assert.True(created.Succeeded, created.Diagnostic);
        }

        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.ActiveActors,
            typeof(BenchmarkRoomActor),
            "",
            nameof(BenchmarkRoomBehavior.TickAsync),
            interval,
            backlogPolicy)));

        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        observer.StartMeasurement();
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
        elapsed.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;

        return observer.CreateResult(
            "active-room-skipifpending",
            actorCount,
            interval,
            backlogPolicy,
            options,
            iteration,
            elapsed.Elapsed,
            allocated,
            cpu);
    }

    private async Task<TimerBenchmarkResult> RunFixedActorScenarioAsync(
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        int iteration,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var observer = new BenchmarkTickObserver();
        var actorId = ActorId.From($"fixed/{iteration}");
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BenchmarkRoomActor), nameof(BenchmarkRoomBehavior.TickAsync)));
        var created = await lifecycle.CreateLocalAsync<BenchmarkRoomActor>(
            actorId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.True(created.Succeeded, created.Diagnostic);

        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(BenchmarkRoomActor),
            actorId.Value,
            nameof(BenchmarkRoomBehavior.TickAsync),
            interval,
            backlogPolicy)));

        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        observer.StartMeasurement();
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
        elapsed.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;

        return observer.CreateResult(
            "fixed-singleton-coalesce",
            1,
            interval,
            backlogPolicy,
            options,
            iteration,
            elapsed.Elapsed,
            allocated,
            cpu);
    }

    private async Task<TimerBenchmarkResult> RunMissingActorScenarioAsync(
        TimeSpan interval,
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var observer = new BenchmarkTickObserver();
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BenchmarkRoomActor), nameof(BenchmarkRoomBehavior.TickAsync)));
        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(BenchmarkRoomActor),
            "missing-room",
            nameof(BenchmarkRoomBehavior.TickAsync),
            interval,
            backlogPolicy)));

        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        observer.StartMeasurement();
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
        elapsed.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;

        return observer.CreateResult(
            "missing-fixed-actor",
            1,
            interval,
            backlogPolicy,
            options,
            1,
            elapsed.Elapsed,
            allocated,
            cpu);
    }

    private async Task<TimerBenchmarkResult> RunBusyFixedActorScenarioAsync(
        TickBacklogPolicy backlogPolicy,
        TimerBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var observer = new BenchmarkTickObserver();
        var actorId = ActorId.From($"busy/{backlogPolicy}");
        await using var scheduler = new HotfixActorTickScheduler(
            provider.GetRequiredService<IActorRuntime>(),
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer);

        HotfixDispatch.Replace(CreateTickTable(typeof(BusyBenchmarkActor), nameof(BusyBenchmarkBehavior.TickAsync)));
        var created = await lifecycle.CreateLocalAsync<BusyBenchmarkActor>(actorId, cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.True(created.Succeeded, created.Diagnostic);

        scheduler.Apply(CreateSnapshot(new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(BusyBenchmarkActor),
            actorId.Value,
            nameof(BusyBenchmarkBehavior.TickAsync),
            TimeSpan.FromMilliseconds(10),
            backlogPolicy)));

        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        observer.StartMeasurement();
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
        elapsed.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;

        return observer.CreateResult(
            $"busy-fixed-{backlogPolicy.ToString().ToLowerInvariant()}",
            1,
            TimeSpan.FromMilliseconds(10),
            backlogPolicy,
            options,
            1,
            elapsed.Elapsed,
            allocated,
            cpu);
    }

    private static HotfixSnapshot CreateSnapshot(params HotfixActorTickDeclaration[] ticks)
    {
        var feature = new HotfixFeatureDeclaration(
            "timer-benchmark",
            typeof(HotfixActorTickSchedulerPerformanceTests),
            Discoverable: true,
            new Dictionary<string, string>(),
            [],
            ticks,
            [],
            []);

        return new HotfixSnapshot(
            "benchmark",
            "benchmark.dll",
            null,
            DateTimeOffset.UtcNow,
            1,
            [],
            HotfixReloadStatus.Succeeded,
            null,
            null,
            [feature]);
    }

    private static HotfixDispatchTable CreateTickTable(Type actorType, string methodName)
    {
        var behaviorType = actorType == typeof(BusyBenchmarkActor)
            ? typeof(BusyBenchmarkBehavior)
            : typeof(BenchmarkRoomBehavior);
        var method = behaviorType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        var binding = new HotfixMethodBinding(
            HotfixDispatch.CreateKey(
                actorType,
                methodName,
                typeof(ValueTask),
                [typeof(HotfixActorTick)]),
            method,
            actorType,
            typeof(ValueTask),
            [typeof(HotfixActorTick)]);
        return new HotfixDispatchTable(1, [binding]);
    }

    private const string ActorRuntimeKind = "real LakonaActorRuntime";

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private void WriteRuntimeMetadata(TimerBenchmarkOptions options)
    {
        _output.WriteLine($"OS: {Environment.OSVersion}");
        _output.WriteLine($"CPU: {GetCpuModel()}");
        _output.WriteLine($"SDK: {GetDotNetSdkVersion()}");
        _output.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        _output.WriteLine($"BuildConfiguration: {BuildConfiguration}");
        _output.WriteLine($"ProcessArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        _output.WriteLine($"ProcessBitness: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        _output.WriteLine($"LogicalProcessors: {Environment.ProcessorCount}");
        _output.WriteLine($"ActorRuntime: {ActorRuntimeKind}");
        _output.WriteLine($"Mode: {(options.Full ? "full" : "smoke")}");
        _output.WriteLine($"Warmup: {options.Warmup}");
        _output.WriteLine($"Duration: {options.Duration}");
        _output.WriteLine($"Iterations: {options.Iterations}");
        _output.WriteLine($"ActorCounts: {string.Join(", ", options.ActorCounts)}");
    }

    private void WriteResult(TimerBenchmarkResult result)
    {
        _output.WriteLine($"Scenario: {result.Scenario}");
        _output.WriteLine($"Actors: {result.ActorCount}");
        _output.WriteLine($"Interval: {result.Interval.TotalMilliseconds} ms");
        _output.WriteLine($"Backlog policy: {result.BacklogPolicy}");
        _output.WriteLine($"Duration: {result.Duration}");
        _output.WriteLine($"Iteration: {result.Iteration}");
        _output.WriteLine($"Expected opportunities: {result.ExpectedTickOpportunities}");
        _output.WriteLine($"Accepted dispatches: {result.AcceptedDispatches}");
        _output.WriteLine($"Entered ticks: {result.EnteredTicks}");
        _output.WriteLine($"Skipped ticks: {result.SkippedTicks}");
        _output.WriteLine($"Coalesced ticks: {result.CoalescedTicks}");
        _output.WriteLine($"Rejected ticks: {result.RejectedTicks}");
        _output.WriteLine($"P50 latency: {result.P50.TotalMilliseconds:F3} ms");
        _output.WriteLine($"P95 latency: {result.P95.TotalMilliseconds:F3} ms");
        _output.WriteLine($"P99 latency: {result.P99.TotalMilliseconds:F3} ms");
        _output.WriteLine($"Allocated: {result.AllocatedBytes} bytes");
        _output.WriteLine($"CPU: {result.CpuTime}");
    }

    private static string GetCpuModel()
    {
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            ?? Environment.GetEnvironmentVariable("PROCESSOR_MODEL")
            ?? "unavailable";
    }

    private static string GetDotNetSdkVersion()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null || !process.WaitForExit(2_000))
            {
                return "unavailable";
            }

            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return "unavailable";
        }
    }

    private sealed class BenchmarkRoomActor : GameActor
    {
        public long TickCount;
    }

    private sealed class BusyBenchmarkActor : GameActor
    {
        public long TickCount;
    }

    private static class BenchmarkRoomBehavior
    {
        public static ValueTask TickAsync(BenchmarkRoomActor actor, HotfixActorTick tick)
        {
            _ = tick;
            Interlocked.Increment(ref actor.TickCount);
            return default;
        }
    }

    private static class BusyBenchmarkBehavior
    {
        public static ValueTask TickAsync(BusyBenchmarkActor actor, HotfixActorTick tick)
        {
            _ = tick;
            Interlocked.Increment(ref actor.TickCount);
            var stopAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 50;
            while (Stopwatch.GetTimestamp() < stopAt)
            {
            }

            return default;
        }
    }

    private sealed class BenchmarkTickObserver : IHotfixActorTickSchedulerObserver
    {
        private readonly ConcurrentQueue<TimeSpan> _latencies = new();
        private long _accepted;
        private long _rejected;
        private long _skipped;
        private long _coalesced;
        private long _entered;
        private long _measurementStartTimestamp = long.MaxValue;

        public void StartMeasurement()
        {
            while (_latencies.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _accepted, 0);
            Interlocked.Exchange(ref _rejected, 0);
            Interlocked.Exchange(ref _skipped, 0);
            Interlocked.Exchange(ref _coalesced, 0);
            Interlocked.Exchange(ref _entered, 0);
            Interlocked.Exchange(ref _measurementStartTimestamp, Stopwatch.GetTimestamp());
        }

        public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
        {
            if (!IsMeasured(observation.QueuedTimestamp))
            {
                return;
            }

            Interlocked.Increment(ref _accepted);
        }

        public void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result)
        {
            _ = result;
            if (!IsMeasured(observation.QueuedTimestamp))
            {
                return;
            }

            Interlocked.Increment(ref _rejected);
        }

        public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
        {
            if (!IsMeasured(observation.QueuedTimestamp))
            {
                return;
            }

            Interlocked.Increment(ref _skipped);
        }

        public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
        {
            if (!IsMeasured(observation.QueuedTimestamp))
            {
                return;
            }

            Interlocked.Increment(ref _coalesced);
        }

        public void OnTickEntered(HotfixActorTickEntryObservation observation)
        {
            if (!IsMeasured(observation.QueuedTimestamp))
            {
                return;
            }

            Interlocked.Increment(ref _entered);
            _latencies.Enqueue(observation.QueueLatency);
        }

        public TimerBenchmarkResult CreateResult(
            string scenario,
            int actorCount,
            TimeSpan interval,
            TickBacklogPolicy backlogPolicy,
            TimerBenchmarkOptions options,
            int iteration,
            TimeSpan duration,
            long allocatedBytes,
            TimeSpan cpuTime)
        {
            var latencies = _latencies.ToArray();
            Array.Sort(latencies);
            return new TimerBenchmarkResult(
                scenario,
                actorCount,
                interval,
                backlogPolicy,
                iteration,
                duration,
                ExpectedTickOpportunities: (long)Math.Floor(duration.TotalMilliseconds / interval.TotalMilliseconds) * actorCount,
                AcceptedDispatches: Interlocked.Read(ref _accepted),
                EnteredTicks: Interlocked.Read(ref _entered),
                SkippedTicks: Interlocked.Read(ref _skipped),
                CoalescedTicks: Interlocked.Read(ref _coalesced),
                RejectedTicks: Interlocked.Read(ref _rejected),
                P50: Percentile(latencies, 0.50),
                P95: Percentile(latencies, 0.95),
                P99: Percentile(latencies, 0.99),
                allocatedBytes,
                cpuTime,
                options.Full);
        }

        private static TimeSpan Percentile(TimeSpan[] values, double percentile)
        {
            if (values.Length == 0)
            {
                return TimeSpan.Zero;
            }

            var index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
            return values[index];
        }

        private bool IsMeasured(long queuedTimestamp)
        {
            return queuedTimestamp >= Interlocked.Read(ref _measurementStartTimestamp);
        }
    }

    private sealed record TimerBenchmarkOptions(
        bool Full,
        int[] ActorCounts,
        TimeSpan Warmup,
        TimeSpan Duration,
        int Iterations)
    {
        public static TimerBenchmarkOptions FromEnvironment()
        {
            var full = string.Equals(
                Environment.GetEnvironmentVariable("LAKONA_TIMER_BENCHMARK_FULL"),
                "1",
                StringComparison.Ordinal);

            return full
                ? new TimerBenchmarkOptions(
                    true,
                    [100, 1_000, 10_000],
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(10),
                    3)
                : new TimerBenchmarkOptions(
                    false,
                    [100],
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(500),
                    1);
        }
    }

    private sealed record TimerBenchmarkResult(
        string Scenario,
        int ActorCount,
        TimeSpan Interval,
        TickBacklogPolicy BacklogPolicy,
        int Iteration,
        TimeSpan Duration,
        long ExpectedTickOpportunities,
        long AcceptedDispatches,
        long EnteredTicks,
        long SkippedTicks,
        long CoalescedTicks,
        long RejectedTicks,
        TimeSpan P50,
        TimeSpan P95,
        TimeSpan P99,
        long AllocatedBytes,
        TimeSpan CpuTime,
        bool Full);
}
```

- [ ] **Step 2: Run the performance smoke tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter HotfixActorTickSchedulerPerformanceTests --logger "console;verbosity=detailed"
```

Expected: the smoke path runs 100 active actors, a 250 ms fixed singleton, a missing fixed actor, and the busy fixed actor policy checks. Output includes runtime metadata, backlog policy, expected opportunities, dispatch counts, latency percentiles, allocation, and CPU summary lines.

- [ ] **Step 3: Run one local full benchmark pass**

Run:

```powershell
$env:LAKONA_TIMER_BENCHMARK_FULL='1'
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter HotfixActorTickSchedulerPerformanceTests --logger "console;verbosity=detailed"
Remove-Item Env:\LAKONA_TIMER_BENCHMARK_FULL
```

Expected: the full path reports 100, 1,000, and 10,000 active actor scenarios with three iterations each, three 250 ms fixed-singleton iterations, one missing fixed actor scenario, and the busy fixed actor policy checks. If the local machine cannot complete 10,000 actors in a reasonable time, keep the code path and record the observed failure in the task handoff before changing counts.

- [ ] **Step 4: Confirm the local command exists in durable docs**

Verify `docs/actor.md` contains:

```powershell
$env:LAKONA_TIMER_BENCHMARK_FULL='1'
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter HotfixActorTickSchedulerPerformanceTests --logger "console;verbosity=detailed"
Remove-Item Env:\LAKONA_TIMER_BENCHMARK_FULL
```

- [ ] **Step 5: Commit performance coverage**

Run:

```powershell
git add tests\Lakona.Game.Server.Tests\HotfixActorTickSchedulerPerformanceTests.cs docs\actor.md
git commit -m "Add actor tick scheduler performance coverage"
```

### Task 7: Final Verification And Temporary Planning Cleanup

**Files:**
- Delete after implementation is complete: `docs/superpowers/specs/2026-06-30-actor-timer-design.md`
- Delete after implementation is complete: `docs/superpowers/plans/2026-06-30-actor-timer-api-performance.md`

- [ ] **Step 1: Run focused test projects**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "HotfixActorTickSchedulerTests|HotfixActorTickSchedulerPerformanceTests"
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --no-restore
```

Expected: all focused tests pass.

- [ ] **Step 2: Run solution build**

Run:

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected: build passes.

- [ ] **Step 3: Run source search for omitted actor tick method names**

Run:

```powershell
rg -n "ScheduleActorTick<|ScheduleActiveActorTicks<" src tests samples docs
```

Expected: every production/sample/durable-doc schedule call has an explicit fourth or third method-name argument. Historical `docs/superpowers/**` entries can still show old forms until the next step deletes completed temporary planning files.

- [ ] **Step 4: Delete completed temporary superpowers docs**

After durable docs and tests contain the final rules, delete:

```powershell
Remove-Item -LiteralPath docs\superpowers\specs\2026-06-30-actor-timer-design.md
Remove-Item -LiteralPath docs\superpowers\plans\2026-06-30-actor-timer-api-performance.md
```

- [ ] **Step 5: Run final diff checks**

Run:

```powershell
git status --short
git diff --check
```

Expected: no whitespace errors. `git status --short` shows only intentional source, test, docs, version, and deleted temporary planning files.

- [ ] **Step 6: Commit cleanup and final verification**

Run:

```powershell
git add -A
git commit -m "Finish actor timer API and performance baseline"
```

- [ ] **Step 7: Record final validation output in the handoff**

The handoff message must include:

- focused test commands and pass/fail result;
- solution build result;
- performance smoke command result;
- whether the full local benchmark was run and where its output is visible;
- package versions bumped;
- BuildTag decision for Agar and generated starters;
- confirmation that no scheduler optimization was included.
