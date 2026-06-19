# Lakona.Game Actor And Behavior Authoring Rules

## Required Model

Hotfix is mandatory for Lakona.Game server projects. A supported game server has
a stable `Server.App` assembly and a reloadable `Server.Hotfix` assembly.
Framework code, tool output, samples, tests, and documentation must not describe
hotfix as optional for game business logic.

The required model is:

```txt
Server.App actor = stable mailbox identity + stable state
Server.Hotfix behavior = replaceable actor business logic
```

This rule applies to all user-authored game actors, including sample actors.
The internal actor kernel may contain executable actor code because it is
framework infrastructure, not game business logic.

## Assembly Responsibilities

| Location | May contain | Must not contain |
| --- | --- | --- |
| `Shared` | RPC service contracts, client/server DTOs, callback contracts | Actor classes, Behavior classes, server-only actor routing types |
| `Server.App` actor classes | State fields, stable infrastructure dependencies, constructors, `OnActivateAsync`, `OnDeactivateAsync` | Login rules, matchmaking rules, room rules, scoring rules, leaderboard ranking, DTO projection decisions |
| `Server.App` bridge code | Actor id selection, `IActorRuntime` calls, `HotfixDispatch.Invoke`, DI registration, stable service proxies | Game decisions that can change without a stable deployment |
| `Server.Hotfix` services | RPC request orchestration, session-facing business decisions, calls into actors and framework services | Stable RPC proxy registration, long-lived runtime ownership |
| `Server.Hotfix` behaviors | One actor type's business operations, field mutation, DTO projection for that actor | RPC endpoints, background threads, timers, static event subscriptions, cached delegates into old hotfix assemblies |

## Actor Class Rules

A user-authored actor class in `Server.App` must be boring on purpose. It can
declare only these members:

- fields and properties that represent stable state
- constants that describe stable state limits, such as an in-memory ring buffer
  size
- constructors that capture stable services needed by behaviors
- `protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)`
- `protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)`
- nested record or class types that are state containers

Actor lifecycle hooks are for stable initialization and cleanup. If a lifecycle
hook needs game-specific decisions, it must enter the current hotfix Behavior
through `HotfixDispatch.Invoke` before executing those decisions.

A user-authored actor class in `Server.App` must not declare methods such as:

```csharp
public Task<UserLoginResult> LoginAsync(string password, bool reconnect)
public Task<RoomSettlementResult> StartAsync(RoomStartRequest request)
public Task<LeaderboardSnapshot> GetLeaderboardAsync(int topN)
private Task<Dictionary<string, RoomAssignment>> TryMatchAsync(DateTime nowUtc, bool allowExpiredPartialBatch)
```

Those methods are business behavior. They belong in a matching
`Server.Hotfix` Behavior class.

## Behavior Class Rules

Each Behavior corresponds to one actor type and is marked with
`[HotfixBehaviorOf(typeof(TActor))]`.

Behavior methods are public static extension methods whose first parameter is
`this TActor self`:

```csharp
[HotfixBehaviorOf(typeof(RoomActor))]
internal static class RoomBehavior
{
    public static ValueTask<RoomSettlementResult> StartAsync(
        this RoomActor self,
        RoomStartRequest request,
        CancellationToken cancellationToken = default)
    {
        // Business logic lives here.
    }
}
```

Behavior code runs inside an actor turn. It may read and mutate the actor's
stable fields. It should access fields through `internal` members with
`InternalsVisibleTo("Server.Hotfix")` or generated friend accessors. It must not
use runtime reflection for ordinary field access.

Behavior code must not own long-lived runtime resources. Do not store timers,
threads, static event subscriptions, or callbacks in `Server.Hotfix` static
fields.

## Stable Bridge Rules

Stable bridge code may expose application-specific interfaces such as
`IUserStateStore` or `IRoomStateStore` if they are DI boundaries used by hotfix
services. These interfaces are stable hotfix-visible contracts. Changing their
method names, parameters, return types, or DTOs requires a `BuildTag` update.

Bridge implementations must contain routing only:

```csharp
return runtime.AskAsync<UserActor, UserLoginResult>(
    UserId(userId),
    async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask<UserLoginResult>>(
        "LoginAsync",
        actor,
        [typeof(string), typeof(bool)],
        [password, reconnect]).ConfigureAwait(false)).AsTask();
```

The bridge selects the actor id, enters the actor turn through `IActorRuntime`,
and invokes the current hotfix method. It does not validate passwords, choose
match batches, compute ranks, award points, or build user-facing replies.

`Server.Hotfix` services may call Behavior extension methods directly because
the service and Behavior are loaded from the same current hotfix assembly.
`Server.App` code must not call Behavior extension methods directly because
`Server.App` does not reference `Server.Hotfix`.

`Server.App` may reference stable framework packages under
`Lakona.Game.Server.Hotfix*`, including `HotfixDispatch`. It must not reference
the reloadable game hotfix project, assembly, or namespace named
`Server.Hotfix`.

Mandatory hotfix also means stable app code must fail fast when a required
hotfix Behavior is not loaded. Stable app code must not catch
`HotfixMethodNotLoadedException` and continue with duplicate stable game rules.

## Analyzer Requirements

The framework must provide a compile-time diagnostic for the stable actor
boundary:

| Diagnostic | Severity | Condition |
| --- | --- | --- |
| `ULGHOTFIX011` | Error | A user-authored type deriving from `Lakona.Game.Server.Actors.Actor` or `Actor<TKey>` declares an ordinary method other than `OnActivateAsync` or `OnDeactivateAsync` |

The analyzer belongs in `Lakona.Game.Server.Hotfix.Generators` because that
package is already part of the mandatory hotfix toolchain for server app
projects.

Allowed lifecycle method shapes are exact:

```csharp
protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
```

The analyzer must report methods regardless of whether they are public,
internal, protected, private, static, async, or synchronous. Static helper
methods on actor classes are not allowed because they hide game rules in the
stable assembly. Constructors, fields, properties, nested types, and property
accessors are not ordinary methods for this diagnostic.

## Agar Sample Migration Rules

The Agar sample must be migrated to the same model as generated projects.

`samples/Game.Unity.Agar/Server/App/State` actor classes must keep state only.
Move the following business logic to `samples/Game.Unity.Agar/Server/Hotfix`:

- `UserActor` login, profile projection, online status, win count, and victory
  point updates
- `PlayerSessionActor` attach, reconnect, queue, room assignment, disconnect,
  heartbeat, and snapshot projection
- `MatchmakingActor` enqueue, cancel, tick, batch selection, room assignment,
  bot fill, and gateway resolution decisions
- `RoomActor` create, join, leave, ready, start, complete, settlement, and
  snapshot projection
- `LeaderboardActor` ranking, weekly reset, record update, and snapshot
  projection
- `MatchmakingQueuePolicy`, `LeaderboardRankingPolicy`, and
  `LeaderboardPeriodPolicy`
- stable fallback simulation or settlement rules in
  `Server/App/Realtime/RoomRuntime.cs`

The stable `StateStores.cs` file may remain in `Server.App` only as a bridge.
After migration, each state store method must call `HotfixDispatch.Invoke`
inside the actor turn and must not call a business method declared on the actor
class.

## Typed Actor Generation Rule

Managed distributed actor APIs may expose typed actor refs, but they must not
require user-authored business method bodies on stable actor classes. Generated
local or remote dispatch must eventually enter the current hotfix Behavior.

Until the generator supports behavior-first actor contracts, samples must use
`IActorRuntime` plus stable bridge code for actor behavior calls. Do not add
new sample code that depends on business methods declared directly on
`Server.App` actor classes.
