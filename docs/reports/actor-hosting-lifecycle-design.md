# Actor Hosting Lifecycle API Design

Status: active design, approved in discussion.

Date: 2026-07-01

## Summary

Lakona should expose one user-facing actor lifecycle API: `ActorHosting`.
`ActorHosting` creates, ensures, and destroys actors hosted by the current node.
It owns the transaction across local runtime lifecycle, `ActorDirectory`, and
`ActorDirectoryCache`.

The old generated lifecycle surface should be removed, not maintained in
parallel. That includes `[ActorSpawn]`, `[ActorDestroy]`, generated
`RoomActors.SpawnAsync` and `RoomActors.DestroyAsync`, public
`IActorLifecycle`, public local lifecycle result/status types, and
`IActorRuntime.GetOrCreateAsync` / `StopAsync`.

Actor calls remain separate from actor hosting. Generated actor collections such
as `RoomActors` should only expose selectors such as `Get`, `Local`, and
`Remote`. Creating an actor must not return a generated actor ref.

## Scope And Risk Checkpoint

Goal:

- Make actor creation and deletion easy to use and hard to misuse.
- Remove duplicated template code from samples such as `Game.Unity.Agar`.
- Remove old lifecycle APIs so future maintenance only has one public model.

Affected surfaces:

- `src/Lakona.Game.Server/Actors`
- `src/Lakona.Game.Server.Generators`
- `src/Lakona.Game.Server.Hotfix.Abstractions`
- `src/Lakona.Game.Server/Hotfix`
- `src/Lakona.Tool` generated starter templates
- `samples/Game.Unity.Agar`
- `samples/Game.Godot.Chat`
- actor docs, hotfix docs, package README snippets, generator tests, runtime
  tests, sample tests, and tool rendering tests

Coupling assessment:

- The runtime, generator, hotfix feature startup model, samples, and docs are
  strongly coupled. One implementation owner should carry the main change.
- Helper agents are useful only for source scans, mechanical sample migration,
  docs review, and focused test review after the runtime shape compiles.

Compatibility stance:

- Breaking compatibility is acceptable. Lakona is early in development, and
  keeping two lifecycle APIs would increase user confusion and long-term
  maintenance cost.
- The implementation should remove old public APIs decisively instead of
  keeping aliases or compatibility shims.

Validation plan:

- `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`
- `dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj`
- `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj`
- `dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj`
- `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`
- `dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
- source scans for removed API names

Versioning impact:

- This changes shippable source under `src/**`; affected package versions must
  be bumped before release according to `CONTRIBUTING.md`.

## Problem

Current actor creation requires users to coordinate several low-level services:

- `IActorDirectory` registers the global placement route.
- `IActorDirectoryCache` updates local route cache state.
- `IActorLifecycle` creates or destroys the process-local actor cell.

`Game.Unity.Agar` shows the resulting template code clearly. Services and
features manually register a route, handle concurrent placement conflicts,
create the local actor, set cache entries, and roll back route/cache state on
failure.

The generator has a parallel implementation for `[ActorSpawn]` and
`[ActorDestroy]`. This confirms the flow is framework-owned, but it spreads the
same lifecycle transaction across generated code, samples, and user services.

The current model also exposes too many user-facing concepts:

- `[ActorSpawn]` and `[ActorDestroy]` hidden hook attributes
- generated `SpawnAsync` and `DestroyAsync` methods on typed actor collections
- direct `IActorLifecycle` injection
- result/status objects for local lifecycle operations
- `IActorRuntime.GetOrCreateAsync`, which enables implicit creation outside the
  lifecycle API
- `HotfixFeatureContext.EnsureLocalActor`, another user-visible creation path

These APIs make actor creation look like a collection of special cases instead
of one lifecycle operation.

## Goals

- Provide one public lifecycle entry point named `ActorHosting`.
- Make `ActorHosting` local by definition. It hosts actors on the current node.
- Keep placement policy outside `ActorHosting`.
- Make generated actor collections call-only selectors.
- Remove hidden lifecycle hooks and generated lifecycle methods.
- Use success-returning async methods and typed exceptions instead of public
  result/status objects for ordinary users.
- Keep bottom-layer actor kernel spawning internal and unaffected.

## Non-Goals

- Do not add cross-node actor creation to `ActorHosting`.
- Do not make `ActorHosting` choose owner nodes, balance capacity, or send
  feature commands.
- Do not return generated actor refs from create or ensure operations.
- Do not keep compatibility shims for `[ActorSpawn]`, `[ActorDestroy]`,
  `SpawnAsync`, or `DestroyAsync`.
- Do not serialize actor lifecycle state.

## Public API

`ActorHosting` should be a DI singleton registered by
`AddLakonaGameServerActors()` and normal game server hosting. It should be a
concrete class, not a static class and not an interface-first abstraction.

Proposed user-facing shape:

```csharp
public sealed class ActorHosting
{
    public ValueTask CreateAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    public ValueTask EnsureAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    public ValueTask DestroyAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;
}
```

The method names intentionally omit `Local`. `ActorHosting` means current-node
hosting. Cross-node creation is not part of this API.

Framework-only type-based helpers may exist internally if needed, but the daily
public API should be the generic API above.

## Public Surface Closure

`ActorHosting` is not the only lifecycle API if users can still reach lifecycle
methods through runtime escape hatches. The implementation must close these
surfaces in the same change:

- `IActorRuntime` should keep call, diagnostics, and query operations needed by
  generated refs and framework integrations, but it must not expose
  `GetOrCreateAsync` or `StopAsync`.
- `LakonaActorRuntime` should not expose public create, get-or-create, destroy,
  or stop lifecycle methods. If the concrete class remains public for DI or
  diagnostics, lifecycle methods must move behind internal interfaces.
- `ActorContext.Runtime` must not let actor code create or stop other actors.
  Actor-to-actor interaction remains ordinary generated selector calls.
- `ActorHosting` may depend on internal runtime interfaces such as
  `IActorHostingRuntime` for local cell activation, state checks, forced local
  cleanup, and diagnostics. Those interfaces should be internal to the runtime
  assembly and test-visible only where needed.

Source scans should fail the implementation if public code still exposes or
recommends direct lifecycle calls outside `ActorHosting`.

## Semantics

### CreateAsync

`CreateAsync<TActor>` is strict.

- It creates and publishes an actor on the current node.
- It fails if the actor id is already active locally.
- It fails if `ActorDirectory` reports that the actor is owned by another node.
- It succeeds only after local creation and directory/cache publication are
  complete.
- It returns no actor ref.

Expected transaction for non-local-only actors:

1. Acquire an internal per-actor-id operation gate.
2. Check local actor state.
3. Register `actorId -> localNode` in `ActorDirectory`.
4. Create the local actor cell through internal runtime lifecycle.
5. Set `ActorDirectoryCache`.
6. On failure, stop any partially created local actor, unregister the route
   when this call registered it, remove cache state, and rethrow.

Registering before local activation prevents two nodes from concurrently
creating the same distributed actor. It creates a short window where directory
resolution can point to a not-yet-active actor. That window is acceptable, and
callers may see a structured `ActorNotFound` failure until activation finishes.
Business code should create actors before publishing user-visible ids that
would immediately receive traffic.

For `[ActorLocalOnly]` actors, skip directory and cache work and only create the
local actor.

### EnsureAsync

`EnsureAsync<TActor>` is idempotent for the same actor type.

- If the actor already exists locally with the same type, it returns
  successfully after ensuring local route/cache state where applicable.
- If the actor does not exist, it follows the create transaction.
- If the actor id is bound to a different local type, it fails.
- If the directory is owned by another node, it fails and clears stale local
  cache state.

This method is the replacement for repeated sample helper code such as
`EnsureUserActorAsync`, `EnsureLeaderboardActorAsync`, and feature-local actor
startup declarations.

### DestroyAsync

`DestroyAsync<TActor>` deletes local hosting for the actor id.

- It is idempotent when the actor and local route are already gone.
- It should remove route/cache state before stopping the actor so new calls stop
  routing to this node.
- If local stop fails or times out after route removal, it should best-effort
  re-register the route and restore local cache before throwing.
- If `ActorDirectory` says another node owns the actor, `DestroyAsync` must not
  unregister that remote route. It should still remove stale current-node cache
  entries and stop a stale local actor cell of the requested type, because
  `ActorHosting` is current-node hosting cleanup, not global actor deletion.

Expected transaction for non-local-only actors:

1. Acquire the same per-actor-id operation gate.
2. Validate local actor type if an actor is active.
3. Resolve or unregister `actorId -> localNode` from `ActorDirectory`.
4. If the route belongs to another node, leave the remote route intact and
   continue with stale local cleanup only.
5. Remove `ActorDirectoryCache` entries that point to the local node or are
   known stale.
6. Stop and remove the local actor.
7. On stop failure after local-route removal, best-effort restore route/cache
   if the actor may still be
   active, then rethrow.

For `[ActorLocalOnly]` actors, skip directory and cache work and only stop the
local actor.

## Error Model

The public methods return `ValueTask`. Success is represented by normal
completion. Failure is represented by typed exceptions.

Implementation should avoid preserving public result/status types for deleted
lifecycle APIs. Add a small hosting-specific exception family instead of
requiring users to inspect status enums:

| Case | Exception |
| --- | --- |
| Base type for create/ensure/destroy failures | `ActorHostingException` |
| `CreateAsync` finds an already active same-type actor | `ActorAlreadyHostedException` |
| local actor id is bound to a different actor type | `ActorHostingTypeMismatchException` |
| `CreateAsync` or `EnsureAsync` finds a directory owner on another node | `ActorHostedElsewhereException` |
| directory registration, resolution, or unregister is unavailable | `ActorDirectoryUnavailableException` |
| local stop times out or fails during destroy | `ActorHostingStopException` |

Each exception should include actor id, actor type, operation, and local node
where relevant. Existing actor call exceptions remain for actor calls, not
hosting operations.

No public API should require users to inspect `ActorCreateLocalStatus` or
`ActorDestroyLocalStatus`.

## Generated Actor API

Generated actor collections should only expose actor call selectors:

```csharp
rooms.Get(roomId);
rooms.Local(roomId);
rooms.Remote(nodeId, roomId);
```

Distributed actors expose `Get`, `Local`, and `Remote`. `[ActorLocalOnly]`
actors expose `Local` only and must not grow distributed selectors as part of
this cleanup.

The generator should stop producing:

- `SpawnAsync`
- `DestroyAsync`
- lifecycle hook parameter plumbing
- directory/cache/lifecycle transaction code

The generator should stop scanning:

- `[ActorSpawn]`
- `[ActorDestroy]`

Actor initialization and cleanup are ordinary hotfix behavior methods:

```csharp
await actorHosting.CreateAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
await rooms.Local(roomId).OpenAsync(request, cancellationToken);

await rooms.Local(roomId).CloseAsync(request, cancellationToken);
await actorHosting.DestroyAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
```

If initialization fails after creation, the business service should explicitly
decide whether to destroy the actor. That compensation is business behavior,
not hidden framework hook behavior.

## Hotfix Feature Startup

`HotfixFeatureContext.EnsureLocalActor<TActor>(string actorId)` should be
removed as a public creation path.

Features that need startup-local actors should inject `ActorHosting` and call
`CreateAsync` from the feature start hook, then destroy the actor from the
feature stop hook. Hotfix feature lifecycle methods are currently static, so
the example uses `call.Services`:

```csharp
public sealed class MatchmakingFeature : HotfixGameFeature
{
    private const string ActorIdValue = "matchmaking/default";

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        var actorHosting = call.Services.GetRequiredService<ActorHosting>();
        await actorHosting
            .CreateAsync<MatchmakingActor>(ActorId.From(ActorIdValue), call.CancellationToken)
            .ConfigureAwait(false);
        call.State.Items["matchmaking.actor"] = ActorIdValue;
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        if (call.State.Items.Remove("matchmaking.actor", out var value) &&
            value is string actorId)
        {
            var actorHosting = call.Services.GetRequiredService<ActorHosting>();
            await actorHosting
                .DestroyAsync<MatchmakingActor>(ActorId.From(actorId), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
```

This removes the `HotfixLocalActorDeclaration` publication path and makes
feature startup use the same lifecycle API as services and RPC handlers.

Feature-owned startup actors are lifecycle resources. They should use
`CreateAsync` rather than `EnsureAsync` so a feature does not accidentally
claim and later destroy an actor that another feature or service created.

The hotfix runtime must also preserve rollback safety. Candidate feature
startup should run inside an internal actor-hosting rollback scope. Any actor
created by `ActorHosting` during a candidate `StartAsync` must be destroyed if
the start method throws before the feature is marked started. After a start
method completes, the existing candidate rollback path can call the feature's
`StopAsync`; that stop hook must destroy feature-owned actors recorded in
feature state. On successful publication, the actors remain hosted until the
feature is removed or the host stops.

## Cross-Node Creation

`ActorHosting` does not choose placement and does not send remote commands.

Code that owns placement should keep doing this explicitly:

```csharp
var owner = await SelectOwnerNodeAsync(key, services);
if (owner.Node == localNode.NodeId)
{
    await actorHosting.EnsureAsync<UserActor>(ActorId.From(userId), cancellationToken);
}
else
{
    await featureCommands.SendToNodeAsync<EnsureUserActorRequest, EnsureActorReply>(
        owner,
        StateStoreUserActorPlacement.FeatureName,
        request,
        cancellationToken);
}
```

The remote feature command handler should call `ActorHosting.EnsureAsync` on
the owner node.

## Old Code To Delete

Delete these public or generated lifecycle surfaces:

- `ActorSpawnAttribute`
- `ActorDestroyAttribute`
- generated `SpawnAsync`
- generated `DestroyAsync`
- generator `SpawnHook` and `DestroyHook` metadata
- generator lifecycle hook invocation helpers
- generator tests that assert spawn/destroy generation
- public `IActorLifecycle`
- public `ActorCreateLocalResult`
- public `ActorCreateLocalStatus`
- public `ActorDestroyLocalResult`
- public `ActorDestroyLocalStatus`
- public `ActorCreateOptions` if no remaining public API uses it
- public `ActorDestroyOptions` if no remaining public API uses it
- public `IActorRuntime.GetOrCreateAsync<TActor>`
- public `IActorRuntime.StopAsync`
- `HotfixFeatureContext.EnsureLocalActor`
- `HotfixLocalActorDeclaration`
- the old `HotfixLocalActorPublicationParticipant` declaration-based
  implementation; replace it with internal actor-hosting rollback integration
  if rollback cannot be handled entirely by feature lifecycle
- docs and README snippets that recommend any deleted API
- starter templates that emit any deleted API

Do not delete these internal or advanced foundations solely because their names
include spawn:

- internal actor kernel `ActorSystem.SpawnAsync`
- internal actor kernel `ActorSpawner`
- internal actor kernel `ActorSpawnOptions`

Those are mailbox runtime concepts, not user-facing lifecycle API.

## Migration Examples

### Agar state-store feature

Current code manually injects `IActorLifecycle`, `IActorDirectory`,
`IActorDirectoryCache`, and `LocalActorNodeIdentity`. New code should inject
`ActorHosting` and delegate the local transaction:

```csharp
public sealed class StateStoreFeature(
    ActorHosting actorHosting,
    ILogger<StateStoreFeature> logger) : HotfixGameFeature
{
    public async ValueTask<EnsureActorReply> EnsureUserActorAsync(
        HotfixFeatureCommandCall<EnsureUserActorRequest> call)
    {
        await actorHosting.EnsureAsync<UserActor>(
            ActorId.From(call.Request.UserId),
            call.CancellationToken);

        logger.LogDebug("User actor {UserId} is hosted locally.", call.Request.UserId);
        return new EnsureActorReply { Succeeded = true, Message = "Actor ready." };
    }
}
```

### Matchmaking room creation

Room creation should be explicit:

```csharp
await actorHosting.CreateAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
await rooms.Local(new RoomId(roomId)).OpenAsync(request, cancellationToken);
```

If `OpenAsync` fails and the room should not remain allocated, the service owns
the compensation:

```csharp
try
{
    await actorHosting.CreateAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
    await rooms.Local(new RoomId(roomId)).OpenAsync(request, cancellationToken);
}
catch
{
    await actorHosting.DestroyAsync<RoomActor>(ActorId.From(roomId), CancellationToken.None);
    throw;
}
```

## Documentation Updates

Update `docs/actor.md` to say:

- `ActorHosting` is the only public actor lifecycle API.
- `ActorHosting` is current-node hosting only.
- actor refs and timer callbacks never create actors.
- actor initialization and cleanup are normal hotfix behavior methods.
- generated actor collections expose only `Get`, `Local`, and `Remote`.

Update hotfix and tool docs to remove `EnsureLocalActor` examples and replace
them with feature `StartAsync` / `StopAsync` plus `ActorHosting.CreateAsync` /
`DestroyAsync` for feature-owned startup actors.

## Testing Strategy

Runtime tests:

- `CreateAsync` registers directory, creates local actor, and sets cache.
- `CreateAsync` rolls back directory/cache/local state on local creation
  failure.
- `CreateAsync` fails when the actor already exists locally.
- `EnsureAsync` is idempotent for same-type local actors.
- `EnsureAsync` fails for local type mismatch.
- `EnsureAsync` fails and clears stale cache when the directory is owned by
  another node.
- `DestroyAsync` unregisters route, clears cache, and stops local actor.
- `DestroyAsync` is idempotent when actor and route are absent.
- `DestroyAsync` leaves remote-owned directory routes intact while removing
  stale current-node cache and stale current-node actor cells.
- concurrent create/ensure/destroy calls for the same actor id are serialized.

Generator tests:

- generated collections include `Get`, `Local`, and `Remote`.
- local-only generated collections include only `Local`.
- generated collections do not include `SpawnAsync` or `DestroyAsync`.
- `[ActorSpawn]` and `[ActorDestroy]` no longer appear in generated API tests.

Hotfix/tool/sample tests:

- feature startup examples call `ActorHosting.CreateAsync` and
  `ActorHosting.DestroyAsync` for feature-owned actors.
- feature-owned startup actors are destroyed by feature `StopAsync`.
- actors created during failed candidate `StartAsync` are rolled back.
- generated starter templates do not emit `EnsureLocalActor`.
- Agar business logic tests use `ActorHosting`.
- source scans fail on deleted public API names.

## Completion Criteria

The change is complete when:

- `ActorHosting` is the only user-facing actor lifecycle entry point.
- public runtime and actor context APIs no longer expose create/get-or-create or
  stop lifecycle methods.
- generated actor collections no longer contain lifecycle methods.
- hook attributes and their generator handling are deleted.
- hotfix feature startup uses `ActorHosting` instead of `EnsureLocalActor` and
  preserves rollback/stop cleanup for feature-owned actors.
- samples and starter templates compile with the new API.
- source scans find no references to deleted lifecycle APIs outside changelog or
  intentional migration notes for the active branch.
- affected tests pass or skipped validations are explicitly documented.
