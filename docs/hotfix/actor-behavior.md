# Hotfix Actor And Behavior Authoring Rules

## Required Model

Hotfix is mandatory for Lakona server projects. A supported game server has a
stable `Server.App` assembly and a reloadable `Server.Hotfix` assembly.
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
| `Server.App` runtime adapter code | generated hotfix service proxies, actor runtime infrastructure, `BuildTag` and local admin metadata, framework `IHotfixServiceInvoker` calls by numeric method id | Game decisions that can change without a stable deployment, user-authored business lifecycle handlers, lifecycle runtime contract files, application-specific Feature classes, hosted matchmaking loops, room runtimes, hand-written string dispatch into hotfix actor methods |
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
public Task<LeaderboardSnapshot> GetLeaderboardAsync(LeaderboardQueryRequest request)
private Task<Dictionary<string, RoomAssignment>> TryMatchAsync(DateTime nowUtc, bool allowExpiredPartialBatch)
```

Those methods are business behavior. They belong in a matching
`Server.Hotfix` Behavior class.

## Behavior Class Rules

Each user-authored Behavior corresponds to exactly one actor type and is marked
with `[HotfixBehaviorOf(typeof(TActor))]`. `TActor` must derive from
`Lakona.Game.Server.Actors.Actor<TKey>`.

The Behavior class must be a `static partial` class named
`<ActorPrefix>Behavior`, where `<ActorPrefix>` is the actor class name without
the `Actor` suffix. The same partial type may be split across files, but a
second Behavior type for the same Actor is invalid.

Behavior methods are public static extension methods whose first parameter is
`this TActor self`:

```csharp
[HotfixBehaviorOf(typeof(RoomActor))]
internal static partial class RoomBehavior
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
framework services that need to react to runtime facts should use
framework-owned lifecycle bridges. User-authored runtime loops such as
matchmaking, room updates, and room settlement are created by feature lifecycle
as LakonaTimer callbacks that enqueue actor behavior methods.

Generated framework RPC service proxies may also call hotfix services through
`IHotfixServiceInvoker`; that is the supported service binding model. The
forbidden pattern is sample-authored stable app code that wraps actor behavior
behind business store interfaces or string method names.

`Server.Hotfix` service code should enter actor turns through generated
behavior-first actor selectors and call DTO-shaped Behavior contracts:

```csharp
var result = await users
    .Get(new UserId(account))
    .LoginAsync(new UserLoginRequest
    {
        Password = request.Password,
        Reconnect = request.Reconnect
    });
```

Hotfix feature descriptors configure feature services and command handlers.
Feature-owned startup actors are created from lifecycle hooks through the
current-node `ActorHosting` entry point. Periodic work is created through the
stable timer entry point:

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .CreateAsync<MatchmakingActor>(ActorId.From("default"), call.CancellationToken);

        var timerId = await LakonaTimer.CreatePeriodicTimerAsync<BattleRuntimeTimers, BattleRuntimeTick>(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50),
            nameof(BattleRuntimeTimers.TickAsync),
            new BattleRuntimeTick("default"),
            call.CancellationToken);

        call.State.Items["battle-runtime.timer"] = timerId;
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        await call.Services
            .GetRequiredService<ActorHosting>()
            .DestroyAsync<MatchmakingActor>(ActorId.From("default"), CancellationToken.None);

        if (call.State.Items.TryGetValue("battle-runtime.timer", out var value) &&
            value is TimerId timerId)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None);
        }

        call.State.Items.Remove("battle-runtime.timer");
    }
}

public sealed record BattleRuntimeTick(string QueueId);
```

Feature `StopAsync` should destroy timers even if the stop request token has
already been canceled. Use a noncancelable cleanup token, such as
`CancellationToken.None`, when deleting feature-owned timers.

The timer scheduler supplies stable context and resolves callbacks against the
current hotfix behavior table. Stable App code does not validate passwords,
choose match batches, compute ranks, award points, build user-facing replies, or
call actor behavior one step at a time.

Framework-owned lifecycle bridges follow the same rule. The zero-template host
enables stable lifecycle bridges through framework defaults; generated and
sample App code must not contain user-authored session lifecycle handlers or
lifecycle runtime contract files. The framework bridge selects a numeric
`[RpcMethod]` id, supplies stable context, and validates the required hotfix
contract when the bridge is enabled.

User-authored session lifecycle behavior is written as `Server.Hotfix` `*Lifecycle` classes, not as `Server.App` lifecycle handlers, runtime contract files, or `*LifecycleService` classes.

Stable `LakonaGameFeature` classes are framework infrastructure and may live in
runtime packages. User-authored game feature declarations live in
`Server.Hotfix` as `HotfixGameFeature` descriptors. Generated and sample
`Server.App` projects must not contain application-specific Feature classes,
hosted matchmaking loops, room runtimes, or feature adapters that raise
project-specific runtime events. Reloadable runtime loops are created by
feature lifecycle as LakonaTimer callbacks that invoke actor behavior methods.

Feature commands are capability-level orchestration points: placement checks,
route registration, local actor creation, and the first calls into actors. Once
a concrete actor exists, business logic should use generated actor refs rather
than treating feature commands as actor mailboxes.

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
- `UserActor` session attach, reconnect, queue, room assignment, disconnect,
  heartbeat, and snapshot projection
- `MatchmakingActor` enqueue, cancel, tick, batch selection, room assignment,
  bot fill, and gateway resolution decisions
- `RoomActor` create, join, leave, ready, start, complete, settlement, and
  snapshot projection
- `LeaderboardActor` ranking, weekly reset, record update, and snapshot
  projection
- `MatchmakingQueuePolicy`, `LeaderboardRankingPolicy`, and
  `LeaderboardPeriodPolicy`
- settlement commit rules in hotfix room actor behavior

The Agar sample must not contain `Server/App/State/StateStores.cs` or replacement
`I*StateStore` business bridges. User-authored runtime loops are created by
hotfix feature lifecycle as LakonaTimer callbacks. Stable App code must not
define application-specific hotfix event adapters, room runtimes, matchmaking
hosted services, or game Feature classes.

## Typed Actor Generation Rule

Managed distributed actor APIs may expose typed actor refs, but they must not
require user-authored business method bodies on stable actor classes. Generated
local or remote dispatch must eventually enter the current hotfix Behavior.

Samples must use generated behavior-first actor selectors for ordinary business
actor calls. Raw `IActorRuntime.AskAsync` and `TellAsync` are framework-level
escape hatches, not a normal service or behavior authoring style. When a node
has already proven local ownership, use the generated `Local(id)` selector.
