# Actor Routed Call API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace generated local-looking actor ref business wrappers with explicit actor-boundary APIs: `Local(id).CallAsync(...)`, `Local(id).PostAsync(...)`, `Route(id).CallAsync(...)`, and `Route(id).PostAsync(...)`. Preserve IDE go-to-definition for real behavior methods by accepting behavior method groups such as `RoomBehavior.JoinAsync`.

**Architecture:** Generated actor collections expose only local and routed actor refs for business code. Behavior method groups are accepted at the public API boundary, immediately resolved to stable generated metadata, and never stored in envelopes, mailboxes, timers, caches, diagnostics, or route state. `CallAsync` is completion-aware; `PostAsync` is acceptance-only. Feature command APIs remain unchanged in this phase.

**Tech Stack:** C#/.NET, Roslyn source generators, xUnit, PowerShell, Lakona actor runtime, Lakona hotfix runtime, Game.Unity.Agar sample.

---

## Scope Checkpoint

This is a large cross-cutting change because it touches public generated APIs, hotfix source generation, runtime actor dispatch semantics, sample code, and documentation. Keep the scope deliberately narrow:

- Keep `HotfixFeatureAttribute`, `HotfixGameFeature`, feature command handlers, and feature command clients unchanged.
- Do not merge Feature and Actor implementations in this plan.
- Remove the business-facing `Get(id).SomeMethod(...)` shape from generated actor refs.
- Remove the business-facing `Remote(nodeId, id).SomeMethod(...)` shape from generated actor refs.
- Keep `Local(id)` for code that has already proven current-process actor ownership.
- Add `Route(id)` for ordinary cross-actor access when the caller does not prove current-process ownership.
- Do not add a second public post API; `PostAsync` is the only fire-and-forget user-facing method.
- Keep pinned-node actor invocation as internal runtime plumbing, test-only helpers, or cluster infrastructure. Do not expose it as a generated business API.

## File Map

Modify these files:

- `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
  - Stop emitting same-named behavior wrapper extension methods.
  - Generate `Local(id)` and `Route(id)` collection methods.
  - Generate typed local and route refs with `CallAsync` and `PostAsync`.
  - Generate unload-safe behavior method metadata resolution.
  - Remove generated public `Remote(NodeId, id)` refs from business-facing actor collections.

- `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/HotfixActorBehaviorDelegates.cs`
  - Add delegate types used by generated `CallAsync` and `PostAsync` overloads.

- `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/HotfixActorBehaviorMethod.cs`
  - Add the stable metadata value used after method-group resolution.

- `src/Lakona.Game.Server.Hotfix/Actors/RemoteActorInvocation.cs`
  - Add no-result call mode support only if existing remote ask/tell abstractions cannot represent a completion-aware `ValueTask` actor call.

- `src/Lakona.Game.Server.Hotfix/Actors/RemoteActorInvoker.cs`
  - Ensure `CallAsync` for `ValueTask` behavior methods can wait for remote completion instead of only remote acceptance.

- `src/Lakona.Game.Server.Generators/TypedActorGenerator.cs`
  - Align stable actor generator output with the public API shape if this generator still emits `Get`, `Remote`, or same-named actor wrappers.

- `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`
  - Update scaffolded hotfix samples to use `Route`/`Local` plus `CallAsync`/`PostAsync`.

- `src/Lakona.Tool/Rendering/Server/GeneratedProjectGuideRenderer.cs`
  - Update generated project guide text and snippets.

- `samples/Game.Unity.Agar/Server/Hotfix/Services/LoginService.cs`
  - Replace `_users.Get(...).MethodAsync(...)` with `_users.Route(...).CallAsync(UserBehavior.MethodAsync, ...)`.

- `samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs`
  - Replace user, room, leaderboard actor calls with explicit route/local refs.

- `samples/Game.Unity.Agar/Server/Hotfix/Behaviors/MatchmakingBehavior.cs`
  - Replace cross-actor calls with route refs while preserving direct `self` calls only inside the same actor.

- `samples/Game.Unity.Agar/Server/Hotfix/Battle/BattleRuntimeFeature.cs`
  - Use `Local(roomId).CallAsync(RoomBehavior.CreateAsync, ...)` and `Local(roomId).CallAsync(RoomBehavior.StartAsync, ...)` after local actor creation.

- `samples/Game.Unity.Agar/Server/Hotfix/Battle/BattleRuntimeTimerCallbacks.cs`
  - Replace local tick enqueueing with `Local(roomId).PostAsync(RoomBehavior.RunTickAsync, ...)`.

- `docs/superpowers/specs/2026-07-08-actor-routed-call-api-design.md`
  - Update the spec after implementation decisions are validated.

- `docs/actor.md`
  - Replace `Get`, generated method wrappers, and public `Remote(nodeId, id)` examples.

- `docs/hotfix/actor-behavior.md`
  - Document method-group actor calls and the same-actor direct-call rule.

- `docs/source-generation.md`
  - Document generated `CallAsync`/`PostAsync` refs instead of behavior-owned wrapper extensions.

- `docs/cluster.md`
  - Clarify that routed actor calls use the directory; pinned-node remote calls are infrastructure-level.

- `docs/README.md`
  - Update any actor API summary links or examples that mention old actor refs.

Modify these tests:

- `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`
  - Replace wrapper-generation assertions with routed/local call API assertions.
  - Assert old APIs are absent from generated source.

- `tests/Lakona.Game.Server.Generators.Tests/TypedActorGeneratorTests.cs`
  - Align stable generator assertions if the stable generator remains part of the public actor API.

- `tests/Lakona.Game.Server.Hotfix.Tests/HotfixActorRouteCallTests.cs`
  - Add runtime tests for route/local `CallAsync` and `PostAsync` behavior.

- `tests/Lakona.Game.Server.Hotfix.Tests/HotfixActorUnloadTests.cs`
  - Add or extend unload tests proving method-group calls do not retain behavior delegates, `MethodInfo`, or hotfix assembly load contexts after invocation.

## API Decisions Fixed For This Plan

- Generated collection names:

```csharp
RoomLocalRef local = rooms.Local(roomId);
RoomRouteRef routed = rooms.Route(roomId);
```

- Completion-aware actor calls:

```csharp
JoinRoomResult result = await rooms.Route(roomId).CallAsync(
    RoomBehavior.JoinAsync,
    new JoinRoomRequest(playerId, displayName),
    cancellationToken);

await rooms.Local(roomId).CallAsync(
    RoomBehavior.StartAsync,
    new StartRoomRequest(seed),
    cancellationToken);
```

- Acceptance-only actor posts:

```csharp
await rooms.Local(roomId).PostAsync(
    RoomBehavior.RunTickAsync,
    new RunRoomTickRequest(tick),
    cancellationToken);
```

- Same-actor direct calls remain allowed only while already executing inside that actor:

```csharp
await self.CompleteLoginAsync(request, cancellationToken);
```

- Cross-actor calls must always go through the generated actor collection:

```csharp
await users.Route(userId).CallAsync(
    UserBehavior.AssignRoomAsync,
    new AssignRoomRequest(roomId, nodeId),
    cancellationToken);
```

## Task 1: Lock The Hotfix Generator Contract With Failing Tests

- [ ] Edit `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`.

- [ ] Replace the current wrapper-focused assertions in `Generator_emits_behavior_owned_extensions_for_actor_refs` with assertions for `Local`, `Route`, `CallAsync`, and wrapper absence.

Use this assertion shape:

```csharp
Assert.Contains("public UserLocalRef Local(UserId id)", generated);
Assert.Contains("public UserRouteRef Route(UserId id)", generated);
Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", generated);
Assert.Contains("global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorCall<global::Game.Server.UserActor, TRequest, TResult> method", generated);
Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", generated);
Assert.Contains("global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorPost<global::Game.Server.UserActor, TRequest> method", generated);
Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", generated);
Assert.DoesNotContain("public UserRef Get(UserId id)", generated);
Assert.DoesNotContain("public UserRemoteRef Remote(", generated);
Assert.DoesNotContain("public static global::System.Threading.Tasks.ValueTask LoginAsync(this global::Game.Server.UserRef self", generated);
Assert.DoesNotContain("TryLoginAsync", generated);
```

- [ ] Replace `Generator_emits_behavior_owned_extensions_for_local_and_remote_actor_refs` with a test named `Generator_emits_local_and_route_actor_refs_without_business_wrappers`.

Use this assertion shape:

```csharp
Assert.Contains("readonly partial struct RoomLocalRef", generated);
Assert.Contains("readonly partial struct RoomRouteRef", generated);
Assert.Contains("__lakona_ResolveBehaviorMethod(method", generated);
Assert.Contains("__lakona_CallAsync<TRequest, TResult>(", generated);
Assert.Contains("__lakona_CallAsync<TRequest>(", generated);
Assert.Contains("__lakona_PostAsync<TRequest>(", generated);
Assert.DoesNotContain("readonly partial struct RoomRemoteRef", generated);
Assert.DoesNotContain("public static global::System.Threading.Tasks.ValueTask PingAsync(", generated);
Assert.DoesNotContain("TryPingAsync", generated);
```

- [ ] Add a test named `Generator_rejects_behavior_method_group_from_wrong_actor`.

The generated source assertion should confirm the resolver throws a normal argument error at the public boundary:

```csharp
Assert.Contains("throw new global::System.ArgumentException(\"The supplied behavior method is not a generated actor behavior method for RoomActor.\"", generated);
```

- [ ] Run the focused generator tests and confirm they fail for the expected old API reasons.

Command:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Generators.Tests\Lakona.Game.Server.Hotfix.Generators.Tests.csproj --filter HotfixGeneratorTests
```

Expected before implementation:

```text
Failed!
Assert.Contains() Failure
```

- [ ] Commit only the failing test edits.

Command:

```powershell
git add tests\Lakona.Game.Server.Hotfix.Generators.Tests\HotfixGeneratorTests.cs
git commit -m "Lock actor routed call generator contract"
```

## Task 2: Add Public Delegate And Metadata Support Types

- [ ] Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/HotfixActorBehaviorDelegates.cs`.

Use this file content:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Server.Hotfix.Abstractions.Actors;

public delegate ValueTask HotfixActorPost<in TActor, in TRequest>(
    TActor self,
    TRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask HotfixActorPostNoCancellation<in TActor, in TRequest>(
    TActor self,
    TRequest request);

public delegate ValueTask<TResult> HotfixActorCall<in TActor, in TRequest, TResult>(
    TActor self,
    TRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask<TResult> HotfixActorCallNoCancellation<in TActor, in TRequest, TResult>(
    TActor self,
    TRequest request);
```

- [ ] Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/HotfixActorBehaviorMethod.cs`.

Use this file content:

```csharp
namespace Lakona.Game.Server.Hotfix.Abstractions.Actors;

public readonly record struct HotfixActorBehaviorMethod(
    string MethodName,
    ulong RemoteMethodId,
    bool PassCancellationToken);
```

- [ ] Add the new files to the hotfix abstractions project if the project file uses explicit compile includes. If the project uses SDK-style default compile items, leave the project file unchanged.

Check command:

```powershell
Select-String -Path src\Lakona.Game.Server.Hotfix.Abstractions\Lakona.Game.Server.Hotfix.Abstractions.csproj -Pattern "Compile Include"
```

Expected for SDK-style default compile items:

```text
<no output>
```

- [ ] Run the abstractions build.

Command:

```powershell
dotnet build src\Lakona.Game.Server.Hotfix.Abstractions\Lakona.Game.Server.Hotfix.Abstractions.csproj --no-restore
```

Expected:

```text
Build succeeded.
```

- [ ] Commit the support types.

Command:

```powershell
git add src\Lakona.Game.Server.Hotfix.Abstractions\Actors\HotfixActorBehaviorDelegates.cs src\Lakona.Game.Server.Hotfix.Abstractions\Actors\HotfixActorBehaviorMethod.cs
git commit -m "Add actor behavior call metadata types"
```

## Task 3: Generate Local And Route Refs Instead Of Business Wrappers

- [ ] Edit `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`.

- [ ] Stop appending behavior wrapper extension types from the hotfix output. Remove the call path that emits generated extension methods with the same names as behavior methods.

Search command:

```powershell
rg -n "AppendBehaviorWrapperType|AppendBehaviorWrapperMethod|AppendBehaviorTryTellWrapperMethod" src\Lakona.Game.Server.Hotfix.Generators\HotfixGenerator.cs
```

Expected after implementation:

```text
<no output>
```

- [ ] Rename the generated distributed ref concept from `Ref` to `RouteRef`, and expose it through `Route(id)`.

Generated source target:

```csharp
public RoomLocalRef Local(RoomId id)
{
    return new RoomLocalRef(_runtime, id);
}

public RoomRouteRef Route(RoomId id)
{
    return new RoomRouteRef(_runtime, _remoteInvoker, _directory, id);
}
```

- [ ] Remove the business-facing generated `Remote(NodeId nodeId, TActorId id)` method from actor collections.

Generated source must not contain:

```csharp
public RoomRemoteRef Remote(
```

- [ ] Generate `CallAsync` overloads for behavior methods that return `ValueTask<TResult>`.

Generated source target:

```csharp
public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorCall<RoomActor, TRequest, TResult> method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken = default)
{
    var actorMethod = __lakona_ResolveBehaviorMethod(method, typeof(TRequest), typeof(TResult));
    return __lakona_CallAsync<TRequest, TResult>(actorMethod, request, cancellationToken);
}

public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorCallNoCancellation<RoomActor, TRequest, TResult> method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken = default)
{
    var actorMethod = __lakona_ResolveBehaviorMethod(method, typeof(TRequest), typeof(TResult));
    return __lakona_CallAsync<TRequest, TResult>(actorMethod, request, cancellationToken);
}
```

- [ ] Generate `CallAsync` overloads for behavior methods that return `ValueTask`.

Generated source target:

```csharp
public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorPost<RoomActor, TRequest> method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken = default)
{
    var actorMethod = __lakona_ResolveBehaviorMethod(method, typeof(TRequest), resultType: null);
    return __lakona_CallAsync<TRequest>(actorMethod, request, cancellationToken);
}

public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorPostNoCancellation<RoomActor, TRequest> method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken = default)
{
    var actorMethod = __lakona_ResolveBehaviorMethod(method, typeof(TRequest), resultType: null);
    return __lakona_CallAsync<TRequest>(actorMethod, request, cancellationToken);
}
```

- [ ] Generate `PostAsync` overloads only for behavior methods that return `ValueTask`.

Generated source target:

```csharp
public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorPost<RoomActor, TRequest> method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken = default)
{
    var actorMethod = __lakona_ResolveBehaviorMethod(method, typeof(TRequest), resultType: null);
    return __lakona_PostAsync<TRequest>(actorMethod, request, cancellationToken);
}

public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorPostNoCancellation<RoomActor, TRequest> method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken = default)
{
    var actorMethod = __lakona_ResolveBehaviorMethod(method, typeof(TRequest), resultType: null);
    return __lakona_PostAsync<TRequest>(actorMethod, request, cancellationToken);
}
```

- [ ] Generate a resolver that uses the method group only at the boundary and stores only stable metadata.

Generated source target:

```csharp
private static global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod __lakona_ResolveBehaviorMethod(
    global::System.Delegate method,
    global::System.Type requestType,
    global::System.Type? resultType)
{
    var methodInfo = method.Method;
    var declaringTypeName = methodInfo.DeclaringType?.FullName;
    var methodName = methodInfo.Name;

    if (declaringTypeName == "Game.Server.Hotfix.RoomBehavior"
        && methodName == "JoinAsync"
        && requestType == typeof(JoinRoomRequest)
        && resultType == typeof(JoinRoomResult))
    {
        return new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(
            "JoinAsync",
            123456789UL,
            passCancellationToken: true);
    }

    throw new global::System.ArgumentException(
        "The supplied behavior method is not a generated actor behavior method for RoomActor.",
        nameof(method));
}
```

- [ ] Ensure the resolver does not store `Delegate`, `MethodInfo`, `RuntimeMethodHandle`, behavior `Type`, or hotfix assembly objects in static fields or generated caches.

Search command:

```powershell
rg -n "Dictionary<.*MethodInfo|MethodInfo.*Dictionary|Delegate.*static|RuntimeMethodHandle|MethodHandle" src\Lakona.Game.Server.Hotfix.Generators\HotfixGenerator.cs
```

Expected after implementation:

```text
<no output>
```

- [ ] Generate route ref helpers so `CallAsync<TRequest, TResult>` routes through the existing ask path.

Generated source target:

```csharp
private global::System.Threading.Tasks.ValueTask<TResult> __lakona_CallAsync<TRequest, TResult>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken)
{
    return __lakona_AskAsync<TRequest, TResult>(
        method.MethodName,
        method.RemoteMethodId,
        method.PassCancellationToken,
        request,
        cancellationToken);
}
```

- [ ] Generate local ref helpers so `PostAsync<TRequest>` enqueues locally and returns after acceptance.

Generated source target:

```csharp
private global::System.Threading.Tasks.ValueTask __lakona_PostAsync<TRequest>(
    global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,
    TRequest request,
    global::System.Threading.CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    var result = __lakona_TryTell<TRequest>(
        method.MethodName,
        method.PassCancellationToken,
        request);
    return result == global::Lakona.Game.Server.Actors.ActorTellResult.Accepted
        ? default
        : throw __lakona_CreatePostException(result);
}
```

- [ ] Implement `__lakona_TryTell<TRequest>` using the existing generated local tell helper pattern that already passes method name and cancellation-token mode as parameters.

- [ ] Run the focused hotfix generator tests.

Command:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Generators.Tests\Lakona.Game.Server.Hotfix.Generators.Tests.csproj --filter HotfixGeneratorTests --no-restore
```

Expected after implementation:

```text
Passed!
```

- [ ] Commit the hotfix generator change.

Command:

```powershell
git add src\Lakona.Game.Server.Hotfix.Generators\HotfixGenerator.cs tests\Lakona.Game.Server.Hotfix.Generators.Tests\HotfixGeneratorTests.cs
git commit -m "Generate actor routed call refs"
```

## Task 4: Make No-Result CallAsync Completion-Aware

- [ ] Inspect the current hotfix remote invocation path.

Command:

```powershell
rg -n "RemoteActorInvocation|RemoteActorInvoker|AskAsync|TellAsync|ActorTell" src\Lakona.Game.Server.Hotfix src\Lakona.Game.Server.Actors tests\Lakona.Game.Server.Hotfix.Tests
```

- [ ] Add explicit `CallAsync<TRequest>` runtime support for `ValueTask` behavior methods.

Runtime target:

```csharp
public async ValueTask CallAsync<TRequest>(
    ActorId actorId,
    string methodName,
    ulong methodId,
    bool passCancellationToken,
    TRequest request,
    CancellationToken cancellationToken)
{
    RemoteActorInvocationResult result = await InvokeAsync(
        actorId,
        methodName,
        methodId,
        passCancellationToken,
        request,
        expectsReply: true,
        cancellationToken).ConfigureAwait(false);

    result.ThrowIfNotCompleted();
}
```

- [ ] Keep `PostAsync<TRequest>` on the tell/accepted path.

Runtime target:

```csharp
public async ValueTask PostAsync<TRequest>(
    ActorId actorId,
    string methodName,
    ulong methodId,
    bool passCancellationToken,
    TRequest request,
    CancellationToken cancellationToken)
{
    RemoteActorInvocationResult result = await InvokeAsync(
        actorId,
        methodName,
        methodId,
        passCancellationToken,
        request,
        expectsReply: false,
        cancellationToken).ConfigureAwait(false);

    result.ThrowIfNotAccepted();
}
```

- [ ] Add `tests/Lakona.Game.Server.Hotfix.Tests/HotfixActorRouteCallTests.cs`.

Required test cases:

```csharp
[Fact]
public async Task Route_CallAsync_value_task_waits_for_behavior_completion()
```

Required assertions:

```csharp
Assert.False(callTask.IsCompleted);
completionGate.SetResult();
await callTask.WaitAsync(TimeSpan.FromSeconds(5));
Assert.Equal(expectedMutation, await stateReader.ReadAsync());
```

```csharp
[Fact]
public async Task Route_PostAsync_value_task_returns_after_acceptance()
```

Required assertions:

```csharp
await postTask.WaitAsync(TimeSpan.FromSeconds(5));
Assert.False(executedTask.IsCompleted);
executionGate.SetResult();
await executedTask.WaitAsync(TimeSpan.FromSeconds(5));
```

```csharp
[Fact]
public async Task Local_PostAsync_dead_actor_throws_actor_call_exception()
```

Required assertions:

```csharp
ActorCallException exception = await Assert.ThrowsAsync<ActorCallException>(
    async () => await rooms.Local(roomId).PostAsync(RoomBehavior.PingAsync, request, CancellationToken.None));
Assert.Contains(roomId.ToString(), exception.Message);
Assert.Contains("Dead", exception.Message, StringComparison.OrdinalIgnoreCase);
```

- [ ] Implement the tests with the existing test host helpers already used by neighboring hotfix actor runtime tests.

- [ ] Run the focused runtime tests.

Command:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --filter HotfixActorRouteCallTests --no-restore
```

Expected:

```text
Passed!
```

- [ ] Commit runtime support and tests.

Command:

```powershell
git add src\Lakona.Game.Server.Hotfix tests\Lakona.Game.Server.Hotfix.Tests
git commit -m "Support completion-aware actor calls"
```

## Task 5: Prove Hotfix Unload Safety

- [ ] Edit the repository’s existing hotfix unload test file. Use `rg -n "Unload|AssemblyLoadContext|WeakReference" tests\Lakona.Game.Server.Hotfix.Tests` to identify it.

- [ ] Add a test that calls a behavior method through `Route(id).CallAsync(Behavior.MethodAsync, ...)`, unloads the hotfix assembly load context, forces collection, and asserts the old load context is collectible.

Test skeleton:

```csharp
[Fact]
public async Task Routed_CallAsync_does_not_retain_behavior_method_group()
{
    WeakReference unloadReference = await RunHotfixCallAndUnloadAsync(async hotfix =>
    {
        await hotfix.Rooms.Route(hotfix.RoomId).CallAsync(
            hotfix.RoomBehavior.JoinAsync,
            hotfix.JoinRequest,
            CancellationToken.None);
    });

    ForceFullCollection();

    Assert.False(unloadReference.IsAlive);
}
```

- [ ] Add a matching test for `Local(id).PostAsync(Behavior.MethodAsync, ...)`.

Test skeleton:

```csharp
[Fact]
public async Task Local_PostAsync_does_not_retain_behavior_method_group()
{
    WeakReference unloadReference = await RunHotfixCallAndUnloadAsync(async hotfix =>
    {
        await hotfix.Rooms.Local(hotfix.RoomId).PostAsync(
            hotfix.RoomBehavior.RunTickAsync,
            hotfix.TickRequest,
            CancellationToken.None);
    });

    ForceFullCollection();

    Assert.False(unloadReference.IsAlive);
}
```

- [ ] Use existing unload helpers for `RunHotfixCallAndUnloadAsync` and `ForceFullCollection`. If the repository uses different helper names, map them one-for-one to the semantics shown in the skeleton: run the actor call, unload the hotfix context, force full collection, assert `WeakReference.IsAlive` is false.

- [ ] Run the unload tests repeatedly.

Command:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --filter "Unload" --no-restore
```

Expected:

```text
Passed!
```

- [ ] Add a source scan guard that fails if generated code introduces static delegate or method-info caches for actor calls.

Test assertion shape:

```csharp
Assert.DoesNotContain("static readonly global::System.Delegate", generated);
Assert.DoesNotContain("static readonly global::System.Reflection.MethodInfo", generated);
Assert.DoesNotContain("RuntimeMethodHandle", generated);
Assert.DoesNotContain("MethodHandle", generated);
```

- [ ] Commit unload tests and any resolver fixes.

Command:

```powershell
git add tests\Lakona.Game.Server.Hotfix.Tests tests\Lakona.Game.Server.Hotfix.Generators.Tests src\Lakona.Game.Server.Hotfix.Generators
git commit -m "Verify actor method groups do not block hotfix unload"
```

## Task 6: Align The Stable Typed Actor Generator

- [ ] Inspect the stable generator API surface.

Command:

```powershell
rg -n "public .* Get\\(|public .* Remote\\(|Try.*Async|Append.*Remote|Append.*Get|ActorRef" src\Lakona.Game.Server.Generators tests\Lakona.Game.Server.Generators.Tests
```

- [ ] Update `TypedActorGenerator` to the same public shape:

```csharp
rooms.Local(roomId).CallAsync(RoomActor.JoinAsync, request, cancellationToken);
rooms.Route(roomId).CallAsync(RoomActor.JoinAsync, request, cancellationToken);
rooms.Local(roomId).PostAsync(RoomActor.RunTickAsync, request, cancellationToken);
```

- [ ] For stable actors that do not use behavior extension classes, use actor method delegates with equivalent stable-actor names:

```csharp
public delegate ValueTask ActorPost<in TActor, in TRequest>(
    TActor self,
    TRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask<TResult> ActorCall<in TActor, in TRequest, TResult>(
    TActor self,
    TRequest request,
    CancellationToken cancellationToken);
```

- [ ] Update `tests/Lakona.Game.Server.Generators.Tests/TypedActorGeneratorTests.cs`.

Assertion shape:

```csharp
Assert.Contains("public RoomLocalRef Local(RoomId id)", generated);
Assert.Contains("public RoomRouteRef Route(RoomId id)", generated);
Assert.Contains("CallAsync<", generated);
Assert.Contains("PostAsync<", generated);
Assert.DoesNotContain("public RoomRef Get(RoomId id)", generated);
Assert.DoesNotContain("public RoomRemoteRef Remote(", generated);
Assert.DoesNotContain("TryJoinAsync", generated);
```

- [ ] Run the stable generator tests.

Command:

```powershell
dotnet test tests\Lakona.Game.Server.Generators.Tests\Lakona.Game.Server.Generators.Tests.csproj --filter TypedActorGeneratorTests --no-restore
```

Expected:

```text
Passed!
```

- [ ] Commit the stable generator alignment.

Command:

```powershell
git add src\Lakona.Game.Server.Generators tests\Lakona.Game.Server.Generators.Tests
git commit -m "Align typed actor generator with routed call API"
```

## Task 7: Migrate Game.Unity.Agar

- [ ] Update `samples/Game.Unity.Agar/Server/Hotfix/Services/LoginService.cs`.

Expected call shape:

```csharp
UserSnapshot snapshot = await _users.Route(userId).CallAsync(
    UserBehavior.GetSnapshotAsync,
    new GetUserSnapshotRequest(userId),
    cancellationToken);

await _users.Route(userId).CallAsync(
    UserBehavior.LoginAsync,
    new LoginRequest(connectionId, displayName),
    cancellationToken);
```

- [ ] Update `samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs`.

Expected room leave shape:

```csharp
await _rooms.Route(roomId).CallAsync(
    RoomBehavior.LeaveAsync,
    new LeaveRoomRequest(playerId, reason),
    cancellationToken);
```

Expected leaderboard shape:

```csharp
LeaderboardSnapshot leaderboard = await _leaderboards.Route(leaderboardId).CallAsync(
    LeaderboardBehavior.GetLeaderboardAsync,
    new GetLeaderboardRequest(limit),
    cancellationToken);
```

- [ ] Update `samples/Game.Unity.Agar/Server/Hotfix/Behaviors/MatchmakingBehavior.cs`.

Cross-actor call target:

```csharp
await users.Route(userId).CallAsync(
    UserBehavior.AssignRoomAsync,
    new AssignRoomRequest(roomId, roomNodeId),
    cancellationToken);
```

Same-actor call target:

```csharp
await self.RequeueAsync(request, cancellationToken);
```

- [ ] Update `samples/Game.Unity.Agar/Server/Hotfix/Battle/BattleRuntimeFeature.cs`.

Local owner call target:

```csharp
await _rooms.Local(roomId).CallAsync(
    RoomBehavior.CreateAsync,
    new CreateRoomRequest(roomId, mapSeed),
    cancellationToken);

await _rooms.Local(roomId).CallAsync(
    RoomBehavior.StartAsync,
    new StartRoomRequest(startedAt),
    cancellationToken);
```

- [ ] Update `samples/Game.Unity.Agar/Server/Hotfix/Battle/BattleRuntimeTimerCallbacks.cs`.

Timer post target:

```csharp
await rooms.Local(roomId).PostAsync(
    RoomBehavior.RunTickAsync,
    new RunRoomTickRequest(tick),
    cancellationToken);
```

- [ ] Search for old actor call shapes in Agar.

Command:

```powershell
rg -n "\\.Get\\(|\\.Remote\\(|Try[A-Z].*Async\\(|\\.JoinAsync\\(|\\.LeaveAsync\\(|\\.RunTickAsync\\(" samples\Game.Unity.Agar\Server\Hotfix
```

Expected after migration:

```text
<no old generated actor-ref business wrapper call sites>
```

- [ ] Run the Agar server build.

Command:

```powershell
dotnet build samples\Game.Unity.Agar\Server\Server.App\Server.App.csproj --no-restore
```

Expected:

```text
Build succeeded.
```

- [ ] Run the dedicated Agar three-node smoke test after the build is green.

Command:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\game\ci\test-agar-three-node.ps1
```

Expected:

```text
PASS
```

- [ ] Commit the Agar migration.

Command:

```powershell
git add samples\Game.Unity.Agar
git commit -m "Migrate Agar actors to routed call API"
```

## Task 8: Update Scaffolding And Documentation

- [ ] Update scaffolding renderers.

Search command:

```powershell
rg -n "Get\\(|Remote\\(|Try[A-Z].*Async|\\.JoinAsync\\(|\\.LoginAsync\\(" src\Lakona.Tool docs
```

- [ ] Replace generated actor examples with these shapes:

```csharp
await rooms.Route(roomId).CallAsync(
    RoomBehavior.JoinAsync,
    request,
    cancellationToken);

await rooms.Local(roomId).PostAsync(
    RoomBehavior.RunTickAsync,
    request,
    cancellationToken);
```

- [ ] Update `docs/actor.md` with a concise boundary rule:

````md
Inside the same actor turn, call the actor instance directly. Across actor boundaries, use the generated collection:

```csharp
await rooms.Route(roomId).CallAsync(RoomBehavior.JoinAsync, request, cancellationToken);
await rooms.Local(roomId).PostAsync(RoomBehavior.RunTickAsync, request, cancellationToken);
```
````

- [ ] Update `docs/hotfix/actor-behavior.md` with method-group navigation guidance:

```md
The first argument to `CallAsync` and `PostAsync` is a behavior method group. IDE go-to-definition lands on the behavior method, not on generated wrapper code. The generated ref resolves the method group to stable metadata immediately and does not retain the delegate.
```

- [ ] Update `docs/source-generation.md` to say the generator emits actor refs and generic call helpers, not same-named business wrappers.

- [ ] Update `docs/cluster.md` to state that `Route(id)` owns directory lookup and node selection.

- [ ] Run the documentation and scaffold scan.

Command:

```powershell
rg -n "actors\\.Get|\\.Remote\\([^)]*node|Try[A-Z].*Async|GeneratedHotfixActorRefMethodAttribute|same-named|wrapper" docs src\Lakona.Tool samples\Game.Unity.Agar
```

Expected after documentation migration:

```text
<only historical design discussion or intentionally named internal code>
```

- [ ] Commit documentation and scaffolding updates.

Command:

```powershell
git add docs src\Lakona.Tool
git commit -m "Document actor routed call API"
```

## Task 9: Repository-Wide Cleanup And Compile

- [ ] Search the repository for public old actor API usage.

Command:

```powershell
rg -n "\\.Get\\([^)]*\\)\\.[A-Z][A-Za-z0-9_]*Async|\\.Remote\\([^)]*\\)\\.[A-Z][A-Za-z0-9_]*Async|Try[A-Z][A-Za-z0-9_]*Async\\(" src tests samples docs
```

Expected:

```text
<no public actor-ref business wrapper usage>
```

- [ ] Search for generated wrapper attributes that became obsolete.

Command:

```powershell
rg -n "GeneratedHotfixActorRefMethodAttribute|GeneratedActorRefMethodAttribute" src tests samples docs
```

Expected:

```text
<only retained compatibility attribute declarations, or no output>
```

- [ ] Remove unused generated-wrapper-only attributes after confirming no runtime or analyzer consumes them.

Removal scan command:

```powershell
rg -n "GeneratedHotfixActorRefMethodAttribute|GeneratedActorRefMethodAttribute" src tests
```

Expected after removal:

```text
<no output>
```

- [ ] Run generator and runtime tests.

Commands:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Generators.Tests\Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Generators.Tests\Lakona.Game.Server.Generators.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
```

Expected:

```text
Passed!
```

- [ ] Run affected sample builds.

Commands:

```powershell
dotnet build samples\Game.Unity.Agar\Server\Server.App\Server.App.csproj --no-restore
dotnet build src\Lakona.Tool\Lakona.Tool.csproj --no-restore
```

Expected:

```text
Build succeeded.
```

- [ ] Run package-impact check for source changes.

Command:

```powershell
git diff --name-only HEAD~8..HEAD -- src
```

Expected:

```text
<list of touched src files used to decide package version bumps>
```

- [ ] Bump versions for every shippable package affected by `src/**` changes according to the repository release rules.

- [ ] Commit cleanup and version changes.

Command:

```powershell
git add src tests samples docs
git commit -m "Remove old actor wrapper API"
```

## Task 10: Final Validation Gate

- [ ] Run the broad test/build validation from a clean working tree state.

Commands:

```powershell
git status --short
dotnet test tests\Lakona.Game.Server.Hotfix.Generators.Tests\Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Generators.Tests\Lakona.Game.Server.Generators.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet build samples\Game.Unity.Agar\Server\Server.App\Server.App.csproj --no-restore
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\game\ci\test-agar-three-node.ps1
```

Expected:

```text
git status --short emits no uncommitted files before validation starts.
Every dotnet command reports Build succeeded or Passed.
The Agar three-node script reports PASS.
```

- [ ] Run final old-API scans.

Commands:

```powershell
rg -n "\\.Get\\([^)]*\\)\\.[A-Z][A-Za-z0-9_]*Async|\\.Remote\\([^)]*\\)\\.[A-Z][A-Za-z0-9_]*Async|Try[A-Z][A-Za-z0-9_]*Async\\(" src tests samples docs
rg -n "static readonly .*Delegate|static readonly .*MethodInfo|RuntimeMethodHandle|MethodHandle" src\Lakona.Game.Server.Hotfix.Generators tests\Lakona.Game.Server.Hotfix.Generators.Tests
```

Expected:

```text
<no output for public old actor API usage>
<no output for unload-unsafe method-group caches>
```

- [ ] Update `docs/superpowers/specs/2026-07-08-actor-routed-call-api-design.md` status from draft to accepted when implementation and validation pass.

Status target:

```md
Status: Accepted after implementation validation
```

- [ ] Commit final spec status.

Command:

```powershell
git add docs\superpowers\specs\2026-07-08-actor-routed-call-api-design.md
git commit -m "Accept actor routed call API design"
```

## Risk Review

- `CallAsync` for `ValueTask` can accidentally behave like `PostAsync` if implemented over the old remote tell path. The runtime tests in Task 4 must prove completion semantics.
- Method-group identity can accidentally retain hotfix assemblies if generated code caches delegates or reflection objects. Task 5 must fail on that regression.
- `Local(id)` can be misused for non-local actors. Keep the API because it is needed for current-process ownership, but make failure explicit and documented.
- Removing generated same-named wrappers is a source-breaking change. Migrate samples, scaffolding, and docs in the same branch so users see one coherent API.
- Removing public pinned-node `Remote(nodeId, id)` can break advanced callers. Keep lower-level pinned invocation available only through infrastructure APIs until a concrete user story justifies a public API.

## Review Checklist

- [ ] Public actor business calls use `Local(id)` or `Route(id)`.
- [ ] Public actor business calls use `CallAsync` or `PostAsync`.
- [ ] Behavior method names are not duplicated as generated actor ref wrapper methods.
- [ ] `PostAsync` exists only for `ValueTask` behavior methods.
- [ ] `CallAsync` supports both `ValueTask<TResult>` and `ValueTask` behavior methods.
- [ ] `CallAsync` waits for behavior completion.
- [ ] `PostAsync` waits only for message acceptance.
- [ ] Method-group delegates are resolved immediately and not retained.
- [ ] Feature command APIs are unchanged.
- [ ] Game.Unity.Agar compiles and passes the dedicated three-node smoke test.
- [ ] Affected package versions are bumped before publishing or merging shippable `src/**` changes.
