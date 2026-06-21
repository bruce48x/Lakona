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
| `Server.App` runtime adapter code | DI registration, actor runtime infrastructure, stable setup calls such as `AddLakonaGameSessionHotfixLifecycle`, `LakonaGameFeature` startup adapters, hosted service timers, room runtime ownership, framework `IHotfixServiceInvoker` calls by numeric method id | Game decisions that can change without a stable deployment, user-authored business lifecycle handlers, lifecycle runtime contract files, hand-written string dispatch into hotfix actor methods |
| `Server.Hotfix` services and lifecycle classes | RPC request orchestration, session-facing business decisions, user-authored `*Lifecycle` classes, calls into actors and framework services | Stable RPC proxy registration, long-lived runtime ownership, `*LifecycleService` classes |
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
hook needs game-specific decisions, it should raise a framework hotfix service
event through a numeric `IHotfixServiceInvoker` method id, and the hotfix service
should call the current Behavior.

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

## Stable Runtime Event Rules

Stable app code must not expose application-specific state-store bridges such as
`IUserStateStore` or `IRoomStateStore` for business actor behavior. Stable
services that need to react to runtime facts, such as matchmaking ticks,
disconnect cleanup, or room settlement, should define a small server-runtime
hotfix contract with explicit `[RpcMethod(id)]` ids and call it through
`IHotfixServiceInvoker` by numeric method id.

Generated framework RPC service proxies may also call hotfix services through
`IHotfixServiceInvoker`; that is the supported service binding model. The
forbidden pattern is sample-authored stable app code that wraps actor behavior
behind business store interfaces or string method names.

`Server.Hotfix` service code should enter actor turns through
`HotfixServiceCall.Actors` and call Behavior methods directly:

```csharp
var result = await call.Actors.AskAsync<UserActor, UserLoginResult>(
    ActorId.From(account),
    (actor, _) => actor.LoginAsync(password, reconnect));
```

Stable runtime event adapters must contain routing only:

```csharp
return hotfix.InvokeAsync<IAgarRuntimeService, HotfixServiceCall<AgarMatchmakingTickRequest>>(
    AgarRuntimeMethodIds.TickMatchmaking,
    new HotfixServiceCall<AgarMatchmakingTickRequest>(
        request,
        string.Empty,
        services,
        actors,
        gameServer));
```

The adapter selects the explicit hotfix service method id and supplies stable
framework context. It does not validate passwords, choose match batches, compute
ranks, award points, build user-facing replies, or call actor behavior one step
at a time.

Framework-owned lifecycle bridges follow the same rule. `Server.App` may enable
stable setup calls such as `AddLakonaGameSessionHotfixLifecycle`, but generated
and sample App code must not contain user-authored session lifecycle handlers or
lifecycle runtime contract files. The framework bridge selects a numeric
`[RpcMethod]` id, supplies stable context, and validates the required hotfix
contract when the bridge is enabled.

User-authored session lifecycle behavior is written as `Server.Hotfix` `*Lifecycle` classes, not as `Server.App` lifecycle handlers, runtime contract files, or `*LifecycleService` classes.

`LakonaGameFeature` classes also stay in `Server.App`. A Feature is a stable
startup and cluster capability adapter, not a hotfix behavior container.
Feature classes may register services, runtime hosts, hosted service loops,
and bridge adapters. Feature classes must not contain game rules. If a Feature
starts a loop such as matchmaking ticks or room runtime settlement, that loop
raises a hotfix runtime service event and the replaceable decision lives in
`Server.Hotfix`.

`Server.App` may reference stable framework packages under
`Lakona.Game.Server.Hotfix*`, including `IHotfixServiceInvoker`. It must not
reference the reloadable game hotfix project, assembly, or namespace named
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
- settlement commit rules in `Server/App/Realtime/RoomRuntime.cs`

The Agar sample must not contain `Server/App/State/StateStores.cs` or replacement
`I*StateStore` business bridges. Stable hosted services and runtime loops should
raise hotfix runtime events; hotfix services and behaviors perform the business
state mutation.

## Typed Actor Generation Rule

Managed distributed actor APIs may expose typed actor refs, but they must not
require user-authored business method bodies on stable actor classes. Generated
local or remote dispatch must eventually enter the current hotfix Behavior.

Until the generator supports behavior-first actor contracts, samples may use
`IActorRuntime` inside hotfix services and behaviors for actor behavior calls.
Do not add new sample code that depends on business methods declared directly on
`Server.App` actor classes, and do not reintroduce stable state-store bridges.
