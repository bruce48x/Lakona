# Agar Hotfix Actor Boundary Design

## Goal

Align `samples/Game.Unity.Agar` with Lakona's stable-state and hotfix-behavior
model by removing server-only contracts from Unity-facing `Shared`, replacing
raw service-level actor dispatch with generated typed actor selectors, and
making the actor id model explicit and consistent.

## Current Problems

`Shared` currently exposes server-only actor DTOs. Unity client code imports
`Shared.Interfaces` and `Shared.Gameplay`; it does not need
`Agar.Sample.State.Contracts.*`. `Shared/State/MatchmakingContracts.cs` still
contains internal matchmaking actor state, enqueue/cancel requests, queue
tickets, results, and room assignment types. `MatchmakingTickRequest` is
unused. `MatchmakingState` itself is server actor state; even when narrowed to
`DefaultRoomSize` and `PendingTickets`, `PendingTickets` is queue internals and
depends on a server-only ticket type.

Hotfix services and behaviors use `IActorRuntime.AskAsync` and
`IActorRuntime.TellAsync` directly. That makes placement intent unclear at the
business call site. It also conflicts with `docs/actor.md`, which states that
normal business code uses generated typed selectors:
`Get(id)`, `Local(id)`, and `Remote(nodeId, id)`.

The current framework generator cannot simply be reused for Agar. It discovers
business methods declared on `Actor<TKey>` classes, but Agar actors are stable
state shells and must not declare business methods. The correct path is a
behavior-first actor contract generator that emits typed selectors from
server-side actor contracts and dispatches into current hotfix behavior.

There is also a concrete actor id inconsistency. `LoginService`,
`PlayerService`, tests, and matchmaking behavior use bare player ids for
`UserActor`. `BattleService` and `AgarSessionLifecycle` currently create
`ActorId.From($"session:{userId}")`, which points at a different actor id.

## Decisions

### Shared Boundary

`Shared` is Unity-facing. It may contain only:

- client/server RPC service contracts, callback contracts, and DTOs under
  `Shared.Interfaces`;
- gameplay simulation types intentionally shared by single-player client code
  and multiplayer server code under `Shared.Gameplay`;
- small protocol value objects that are serialized to Unity clients.

`Shared` must not contain server-only actor request DTOs, actor result DTOs,
actor snapshots used only for server orchestration, stable actor state, or
server diagnostics.

Move the matchmaking actor state and DTOs used only by server hotfix behavior,
server services, and server tests out of `Shared`:

- `MatchmakingState`
- `MatchmakingEnqueueRequest`
- `MatchmakingEnqueueResult`
- `MatchmakingCancelRequest`
- `MatchmakingCancelResult`
- `MatchmakingQueueTicket`
- `RoomAssignment`

Delete `MatchmakingTickRequest`.

Keep `MatchmakingStatusSnapshot` server-only. It already lives under
`Server/Hotfix/State/Matchmaking`; it must not be reintroduced into `Shared`.

Keep server-side `MatchmakingState` with exactly these properties:

- `DefaultRoomSize`
- `PendingTickets`

Delete `Shared/State/MatchmakingContracts.cs` and its Unity `.meta` file. The
Unity client does not import `Agar.Sample.State.Contracts.*`, so there is no
client contract to preserve for this file.

This design does not move every existing server-only room, session, leaderboard,
or user DTO out of `Shared` in the first implementation. It adds tests that
prevent new matchmaking leaks and documents that remaining `Shared/State`
server-only types are debt, not precedent.

### Actor Identity

Agar actor ids are business ids. They must not encode session state, connection
state, transport endpoint state, or callback state.

The stable id shapes are:

- user: bare player account/user id, for example `alice` or `player-1`
- room: bare room id, for example `room-7f...`
- matchmaking queue: `default`
- leaderboard: `current`

`session:{userId}` is not a valid `UserActor` id in Agar. Replace all
`ActorId.From($"session:{userId}")` call sites with the canonical user actor
id.

### Behavior-First Typed Actor Contracts

Add a behavior-first typed actor access model for hotfix-backed actors. The
model must not require business methods on stable actor classes.

Stable actor classes remain state shells:

```csharp
public readonly record struct UserId(string Value);

public sealed class UserActor : Actor<UserId>
{
    internal UserState State = new();
}
```

Server-side actor contracts define callable behavior shape:

```csharp
[HotfixActorContract(typeof(UserActor))]
public interface IUserActorContract
{
    ValueTask<UserLoginResult> LoginAsync(UserLoginRequest request, CancellationToken cancellationToken = default);
    ValueTask<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request, CancellationToken cancellationToken = default);
}
```

The generator emits selectors from the contract:

```csharp
public sealed class UserActors
{
    public UserRef Get(UserId id);
    public UserLocalRef Local(UserId id);
    public UserRemoteRef Remote(NodeId nodeId, UserId id);
}
```

The local generated ref enters the local actor mailbox and invokes the current
hotfix behavior through `HotfixDispatch.Current`. The remote generated ref
serializes the request and sends a cluster actor invocation. The generated
cluster handler deserializes the request, enters the destination actor mailbox,
and invokes `HotfixDispatch.Current` on the destination node.

This preserves the stable-state/hotfix-behavior split:

- `Server.App` actor classes own state and identity.
- `Server.Hotfix` behavior classes own business rules.
- generated refs own placement, serialization, correlation, and dispatch glue.

### Hotfix Service Call Boundary

Hotfix service code must not use `HotfixServiceCall.Actors` as its normal
business actor API. It receives generated actor accessor services through
constructor injection or resolves them from `call.Services` when a static hot
method is intentionally allocation-sensitive.

Service call sites must express placement intent:

- use `Get(id)` for normal business calls where the actor may be local or
  remote;
- use `Local(id)` only when the caller knows the actor is owned by the current
  node;
- use `Remote(nodeId, id)` only when the caller intentionally pins the target
  node.

`BattleService` is the explicit local-owner exception. It may use `Local` for
room calls after validating that the realtime connection is attached to the
runtime owner node. It must not use raw `AskAsync` to express that exception.

### Agar Service Migration

Migrate Agar service orchestration to generated actor accessors.

`LoginService`:

- call `users.Get(new UserId(account)).LoginAsync(...)`;
- call `users.Get(new UserId(loginResult.UserId)).ReconnectAsync(...)`;
- call `users.Get(new UserId(loginResult.UserId)).AttachAsync(...)`.

`PlayerService`:

- call `leaderboards.Get(new LeaderboardId("current"))` for leaderboard reads;
- call `matchmaking.Get(new MatchmakingQueueId("default"))` for enqueue and
  cancel;
- call `rooms.Get(new RoomId(roomId))` for room leave and snapshot reads unless
  a local-only owner has been proven;
- call `users.Get(new UserId(playerId))` for user session mutation.

`AgarSessionLifecycle`:

- use canonical `UserId` and generated user actor accessors for disconnect
  state updates.

`BattleService`:

- use canonical `UserId`;
- use `users.Get(new UserId(playerId))` to validate session authority;
- use `rooms.Local(new RoomId(req.RoomId))` for ready/input calls on the battle
  runtime owner.

Actor behaviors:

- replace helper methods that call `self.Context.Runtime.AskAsync` with
  generated accessor calls resolved from `self.Context.Services`;
- use `Local` only for actors created in the same behavior turn through
  `IActorLifecycle.CreateLocalAsync`.

### Documentation

Update current docs so they say one thing:

- `docs/actor.md` remains the authority for selector semantics.
- `docs/hotfix/actor-behavior.md` must no longer allow Agar hotfix services to
  keep using raw `IActorRuntime` as the normal actor behavior path.
- `samples/Game.Unity.Agar/README.md` must describe generated behavior-first
  selectors and the remaining local-only exception for battle runtime owner
  calls.

## Validation Requirements

Add or update tests to enforce these rules:

- Unity client code must not import `Agar.Sample.State.Contracts.*`.
- `Shared/State/MatchmakingContracts.cs` and
  `Shared/State/MatchmakingContracts.cs.meta` must not exist.
- Server-side `MatchmakingState` must contain exactly `DefaultRoomSize` and
  `PendingTickets`.
- Agar hotfix services must not contain `.AskAsync<` or `.TellAsync<`.
- Agar hotfix behaviors must not contain `.AskAsync<` or `.TellAsync<` except
  in generated framework code.
- Agar source must not contain `session:{userId}` actor id construction.
- A generator test must prove behavior-first actor contracts produce
  `Get`, `Local`, `Remote`, local hotfix dispatch, and remote cluster handler
  dispatch without requiring business methods on the stable actor class.
- Existing distributed topology tests must still prove matchmaking can select a
  battle runtime endpoint, queue when no runtime endpoint exists, and use the
  local KCP endpoint in single-process configuration.

## Non-Goals

This work does not redesign matchmaking rules, room simulation rules,
leaderboard ranking, reliable push, or client UI. It does not move all
remaining server-only `Shared/State` types in one pass. It establishes the
generator and sample rules needed to continue that cleanup safely.

## Risks

The largest risk is changing actor dispatch generation while preserving
existing remote actor semantics. The implementation must keep the existing
typed actor generator tests passing and add behavior-first coverage instead of
weakening current actor call guarantees.

The second risk is moving DTOs out of `Shared` while Unity metadata files still
exist beside old files. Remove obsolete `.meta` files for deleted Unity-facing
files only when the corresponding source file is removed from `Shared`.
