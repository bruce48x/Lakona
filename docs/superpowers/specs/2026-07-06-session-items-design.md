# Game Session Items Design

## Scope Checkpoint

Goal: add a server-side game-session metadata surface so latency-sensitive game
services can cache validated session-local values such as the current room id
without querying a remote business actor on every call.

Affected surfaces:

- `Lakona.Game.Server` session registry, high-level game server API, hotfix call
  context, and focused session tests.
- `docs/session.md` to describe the new contract and ownership boundary.
- `samples/Game.Unity.Agar` to migrate the realtime battle input path after the
  framework API is available.
- `Lakona.Game.Server` package version if implementation changes shippable
  source under `src/Lakona.Game.Server`.

Coupling assessment: the runtime API, registry implementation, hotfix call
context, and Agar migration are strongly coupled because the service dispatch
path needs a stable way to expose the current session and its items. Keep these
changes under one implementation owner.

Independent slices: documentation wording and source scans can be reviewed
independently after the runtime shape compiles. Sample migration should wait
until the session item API and tests are stable.

Compatibility stance: Lakona is early-stage, and `CONTRIBUTING.md` allows clean
breaking changes when they improve the long-term design. Prefer adding a narrow
API rather than preserving an accidental lack of session-local metadata.

Validation plan:

- `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`
- `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj`
- Agar business logic tests that cover realtime attach/input paths if the
  sample migration changes behavior.
- `git diff --check`

Versioning impact: implementation changes under `src/Lakona.Game.Server` require
bumping `src/Lakona.Game.Server/Lakona.Game.Server.csproj`. If only this design
document changes, no package version bump is required.

## Problem

High-frequency game services need fast access to values that are already proven
at session attach time. In the Agar realtime path, `BattleService.SubmitInputAsync`
currently derives the player from `call.CurrentSession` but still queries
`UserActor` to fetch `CurrentRoomId` before forwarding input to the room actor.
When `UserActor` lives on another node, every input frame pays a cross-node
lookup before it can reach the room runtime.

`UserActor` remains the authoritative owner of player session policy, room
assignment, token validation, and disconnect cleanup. The missing framework
capability is a local per-session cache for validated routing metadata.

## Recommended Approach

Add a narrow server-side `GameSessionItems` capability. It should behave like
session-local metadata, not general storage.

The first API should support simple scalar values:

- `string`
- `long`
- `bool`

The implementation can expose these through a small immutable value type such
as `GameSessionItemValue`, plus typed helpers for common reads. Avoid a public
`Dictionary<string, object>` because `object` allows hotfix assembly instances,
delegates, service objects, callbacks, mutable collections, and transport
objects to leak into framework session state.

## API Shape

The high-level API should let business code update and read session items by
`GameSessionKey`:

```csharp
ValueTask SetSessionItemAsync(
    GameSessionKey session,
    string key,
    GameSessionItemValue value,
    CancellationToken cancellationToken = default);

ValueTask<GameSessionItemValue?> GetSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default);

ValueTask RemoveSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default);
```

Hotfix dispatch should expose a snapshot for the current call:

```csharp
public GameSessionItems CurrentSessionItems { get; }
```

`CurrentSessionItems` should be read-only in the call context. Mutation should
go through `call.GameServer` so lifecycle validation and future diagnostics stay
centralized.

## Data Flow

Realtime attach:

1. `BattleService.AttachRealtimeAsync` validates player token, room id, match id,
   and runtime owner against `UserActor`.
2. It starts the realtime game session.
3. It writes session items such as `roomId`, `matchId`, and `runtimeNodeId`.
4. It marks the room member ready through the local room actor.

Realtime input:

1. `BattleService.SubmitInputAsync` reads `playerId` from `call.CurrentSession`.
2. It reads `roomId` from `call.CurrentSessionItems`.
3. It forwards the input directly to the room actor without querying
   `UserActor`.
4. The room actor may still reject stale or unauthorized input using its own
   membership state.

## Lifecycle

Session items are owned by the framework session registry and follow the
`GameSessionKey` lifecycle.

- Created empty with the session.
- Preserved across disconnect and resume while the same session generation is
  resumable.
- Cleared on explicit termination unless terminal state is kept only for resume
  diagnostics.
- Cleared when disconnected sessions expire.
- Never serialized to clients or exposed through shared RPC DTOs.
- Never used as a framework uniqueness constraint.

## Boundaries

Session items are allowed for local cached metadata that was already validated
by authoritative business state.

Allowed examples:

- `roomId`
- `matchId`
- `runtimeNodeId`
- `sessionKind`
- membership or assignment generation numbers

Forbidden examples:

- callback objects
- `RpcSession`, transport objects, or connection objects
- DI services
- actor refs or actor instances
- hotfix-defined class instances
- mutable collections
- account inventory, leaderboard state, durable progress, or room membership
  authority

## Error Handling

Reading a missing item should return `null` rather than throw. Services can
treat absence as an unauthenticated, stale, or not-attached session and return
without forwarding high-frequency work.

Writing or removing items for a missing or terminated session should fail with
the same style as existing session operations: invalid session access is a
server-side programming error, not a client business rejection.

Key validation should reject null, empty, whitespace, and overly long keys.
Keys should use ordinal comparison and should not be emitted as metric tags.

## Testing

Focused tests should cover:

- Set, get, overwrite, and remove session items.
- Items survive disconnect and successful resume.
- Items are removed by expiration.
- Items are removed or inaccessible after termination.
- Hotfix service call context receives a current-session item snapshot.
- Missing current session produces an empty item snapshot.
- Agar realtime input no longer queries `UserActor` on the frame path after
  attach has populated items.

## Self-Review

The design intentionally avoids a public `Dictionary<string, object>` and keeps
business authority in actors. It defines lifecycle behavior, mutation ownership,
allowed value types, validation requirements, and a concrete migration path for
the known Agar frame-path issue. The implementation scope is a single coherent
runtime change plus one sample migration, not a repository-wide storage system.
