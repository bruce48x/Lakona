# Hotfix Actor Behavior Boundary Design

Date: 2026-06-27

## Context

Lakona's hotfix documentation describes this model:

```txt
Server.App actor = stable mailbox identity + stable state
Server.Hotfix behavior = replaceable actor business logic
```

The Agar sample currently violates that model in three related ways:

- Multiple `Server.Hotfix` behavior classes target the same actor. `UserBehavior`
  and `PlayerSessionBehavior` both target `UserActor`.
- `Shared.Gameplay.ArenaSimulation` is not an actor, but it is marked as hotfix
  state and used as a `[HotfixBehaviorOf]` target.
- Generated actor refs expose business methods as instance methods, so ordinary
  calls such as `users.Get(id).MarkQueuedAsync(...)` navigate to
  `GeneratedHotfixActorContracts.g.cs` instead of a behavior boundary.

The combined result is a sample that works at runtime but teaches the wrong
authoring model and creates poor code-reading ergonomics.

## Goals

- Make user-authored actor behavior one-to-one with actor type.
- Keep Shared contracts and gameplay state independent from server actor hotfix
  dispatch.
- Preserve generated local, distributed, and remote actor dispatch support.
- Move the ordinary business-call surface toward behavior-owned APIs.
- Add compile-time and sample source-scan protection so the boundary does not
  regress.
- Keep the migration focused on current Lakona architecture instead of adding
  IDE-specific navigation plugins or editor extensions.

## Non-Goals

- Do not remove generated actor refs or distributed actor dispatch.
- Do not make `Server.App` reference `Server.Hotfix`.
- Do not turn `Shared` gameplay types into server actors.
- Do not build IDE-specific Roslyn, Rider, or language-server navigation
  integrations in this change.
- Do not change the Agar gameplay rules beyond what is required to preserve the
  existing behavior under a cleaner boundary.

## Decision Summary

Use a stricter behavior boundary plus behavior-owned generated wrappers:

```txt
Shared DTOs and gameplay core
  -> Server.App actor state
  -> generated low-level actor refs
  -> Server.Hotfix behavior-owned ref extension wrappers
  -> current hotfix behavior method
```

The generated low-level refs keep ownership of route lookup, local dispatch,
remote dispatch, serialization, and actor call error mapping. The public
business-call surface moves into generated extensions attached to the matching
`partial` behavior class.

## Actor And Behavior Boundary

Each user-authored actor has exactly one primary behavior class:

```txt
UserActor -> UserBehavior
RoomActor -> RoomBehavior
MatchmakingActor -> MatchmakingBehavior
LeaderboardActor -> LeaderboardBehavior
```

Rules:

- `[HotfixBehaviorOf(typeof(TActor))]` may target only a type deriving from
  `Lakona.Game.Server.Actors.Actor<TKey>`.
- A given actor type may have only one `[HotfixBehaviorOf]` class in the hotfix
  assembly.
- The behavior class must be named `<ActorPrefix>Behavior`, where
  `<ActorPrefix>` is the actor name without the `Actor` suffix.
- The behavior class must be `partial` so generated behavior-owned wrappers can
  live in the same class boundary.
- Subdomain code is allowed, but it must be modeled as helpers or policies, not
  as another `*Behavior` for the same actor.
- The same partial behavior type may be split across files for readability, but
  it remains one behavior symbol for one actor.

In Agar, `PlayerSessionBehavior` is not a separate actor behavior. Its public
actor methods move into `UserBehavior`. Any private session-specific helper code
can remain grouped in helper methods or move to a non-behavior helper such as
`PlayerSessionRules`.

## ArenaSimulation Migration

`Shared.Gameplay.ArenaSimulation` becomes a pure shared gameplay core again.

Changes:

- Remove `[HotfixState]` from `ArenaSimulation`.
- Remove `Lakona.Game.Server.Hotfix.*` references from `Shared/Gameplay`.
- Remove `TickWithHotfix` and `SettleMatch` hotfix-dispatch methods from
  `ArenaSimulation`.
- Delete `ArenaSimulationBehavior` as a behavior.
- Rename or replace `ArenaSettlementBehavior` with a normal hotfix helper, such
  as `ArenaSettlementRules`.

The server-side shape becomes:

```txt
Shared ArenaSimulation = deterministic gameplay core
RoomActor = stable room state and persisted simulation state
RoomBehavior = actor turn, tick orchestration, settlement commit, cross-actor updates
ArenaSettlementRules = hotfix helper, not behavior
```

`RoomBehavior.TickAsync` creates `ArenaSimulation` from `RoomState.Simulation`
and calls `Tick(deltaTime)` directly. Settlement remains hotfix-owned by
`RoomBehavior`, either inline or through `ArenaSettlementRules`, and then commits
results to room, user, and leaderboard actors.

## Generated Actor API And Navigation

The current generated refs expose business methods as instance methods. This is
why navigation lands in generated code. The new design separates low-level
dispatch from behavior-owned business methods.

Generated `UserRef`, `UserLocalRef`, and `UserRemoteRef` keep low-level dispatch
APIs needed by generated wrappers. They should not expose ordinary business
method names as instance methods.

Generated behavior-owned wrappers are emitted into the hotfix compilation as
extension methods in the matching partial behavior class. A normal call remains:

```csharp
await users.Get(new UserId(playerId)).MarkQueuedAsync(request);
```

But symbol ownership changes:

```txt
MarkQueuedAsync call
  -> UserBehavior generated ref extension
  -> generated low-level UserRef dispatch
  -> HotfixDispatch
  -> UserBehavior actor-turn method on UserActor
```

The practical navigation promise is:

- Go to Declaration no longer lands on a `UserRef.MarkQueuedAsync` instance
  method in the monolithic generated actor contract file.
- It lands on a behavior-owned wrapper associated with `UserBehavior`.
- Reaching the hand-written actor-turn method may still require one more local
  jump inside the same behavior boundary.

Directly mapping navigation from a generated wrapper to the hand-written actor
method would require IDE-specific navigation plugins, fragile source mapping, or
extra hand-written wrappers. That is intentionally out of scope for this
change.

## Generator And Scanner Changes

`Lakona.Game.Server.Hotfix.Generators` gains authoring diagnostics for behavior
shape:

- Non-actor `[HotfixBehaviorOf]` targets are errors.
- Multiple behavior classes for the same actor are errors.
- Non-partial behavior classes are errors.
- Behavior class names that do not match the target actor prefix are errors.

Generated actor refs are split into two surfaces:

- Stable app output emits ref types, actor selector services, cluster handlers,
  registration, and low-level dispatch helpers.
- Hotfix output emits behavior-owned extension wrappers into matching partial
  behavior classes.

Generated wrapper methods need an internal marker attribute or equivalent
metadata so `HotfixBehaviorScanner` does not register them as actor behavior
methods. The scanner continues to register hand-written public static extension
methods whose first parameter is `this TActor self`.

## Agar Sample Changes

The Agar sample adopts the strict shape:

- `UserBehavior` becomes the single behavior for `UserActor`.
- Session operations currently in `PlayerSessionBehavior` move into
  `UserBehavior` or non-behavior helpers.
- `ArenaSimulationBehavior` is removed.
- `ArenaSettlementBehavior` becomes a normal hotfix helper.
- `RoomBehavior` calls `ArenaSimulation.Tick(...)` and hotfix settlement helper
  methods directly.
- Hotfix service and behavior code keeps using generated behavior-first actor
  selectors for ordinary business calls.

## Documentation And Template Updates

Update these docs to describe the stricter model:

- `docs/hotfix/actor-behavior.md`
- `docs/hotfix/architecture.md`
- `docs/actor.md`
- `src/Lakona.Game.Server.Hotfix/README.md`
- `src/Lakona.Game.Server.Hotfix.Abstractions/README.md`

Update `Lakona.Tool` templates so generated projects create `partial`
`<ActorPrefix>Behavior` classes and do not teach shared gameplay state as a
behavior target.

## Test Plan

Generator and analyzer tests:

- Add tests for non-actor `[HotfixBehaviorOf]` targets.
- Add tests for duplicate behavior classes for one actor.
- Add tests for non-partial behavior classes.
- Add tests for behavior name mismatch.
- Update generated actor-ref tests to assert business instance methods are no
  longer emitted on low-level refs.
- Add tests that behavior-owned wrapper methods are generated into the matching
  partial behavior class.

Runtime scanner tests:

- Ensure generated wrapper methods are ignored by `HotfixBehaviorScanner`.
- Ensure hand-written actor behavior methods continue to register and dispatch.
- Ensure duplicate hand-written hotfix method keys still fail validation.

Agar tests:

- Source-scan that each actor has only one hotfix behavior.
- Source-scan that `Shared/**` does not reference `Lakona.Game.Server.Hotfix`.
- Source-scan that `ArenaSimulation.cs` does not contain `HotfixDispatch`,
  `HotfixState`, or `HotfixBehaviorOf`.
- Source-scan that removed misleading behavior files do not exist as behaviors.
- Existing business logic tests continue to pass.

Validation commands:

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

Run the Agar three-node E2E smoke test if the implementation changes runtime
routing, room tick scheduling, gateway ownership, or network-facing behavior.

## Rollout

This is an early-development breaking cleanup. Compatibility-preserving shims
are not required. The implementation should update framework behavior,
generator tests, sample code, documentation, and templates in one coherent
change so new generated projects and the Agar sample teach the same model.

If the implementation touches shippable source under `src/**`, apply the
repository version-bump rules before publishing or merging.
