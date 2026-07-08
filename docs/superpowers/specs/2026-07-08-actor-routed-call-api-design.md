# Actor Routed Call API Design

## Status

Accepted after implementation validation.

## Goal

Replace generated actor ref methods that mimic local behavior calls with an
explicit actor-call API that keeps actor boundary costs visible and lets normal
IDE navigation jump to the real hotfix behavior method.

The current generated shape:

```csharp
await rooms.Get(roomId).JoinAsync(request, cancellationToken);
await rooms.Local(roomId).JoinAsync(request, cancellationToken);
await rooms.Remote(nodeId, roomId).JoinAsync(request, cancellationToken);
```

must be replaced by an explicit call shape:

```csharp
await rooms.Route(roomId)
    .CallAsync(RoomBehavior.JoinAsync, request, cancellationToken);

await rooms.Local(roomId)
    .CallAsync(RoomBehavior.RunTickAsync, request, cancellationToken);
```

The behavior method name remains visible at the call site, but generated code no
longer emits same-named wrappers that intercept "go to definition".

## Scope

In scope:

- generated hotfix actor ref API shape
- generated method metadata and dispatch support
- local actor mailbox calls
- routed actor calls through `ActorDirectory`
- fire-and-forget `PostAsync` support for void-returning behavior methods
- generated starter and Agar sample migration
- docs and tests covering the new actor call surface

Out of scope:

- merging Feature and Actor concepts
- deleting `HotfixFeatureAttribute`
- deleting `HotfixGameFeature`
- replacing feature command dispatch
- changing actor lifecycle ownership in `ActorHosting`
- changing timer callback declaration by method name
- changing RPC service hotfix proxy generation

Feature commands remain capability-level orchestration. Actor calls remain
concrete actor-id-addressed mailbox calls.

## Scope Checkpoint

Goal: make cross-actor calls explicit through generated `Local(id)` and
`Route(id)` refs plus `CallAsync` / `PostAsync`, while preserving hotfix
behavior methods as the source users navigate to and maintain.

Affected surfaces:

- `Lakona.Game.Server.Hotfix.Generators`
- `Lakona.Game.Server.Generators`, if non-hotfix typed actor generation keeps a
  matching API
- `Lakona.Game.Server` actor runtime support types
- generated starter templates under `Lakona.Tool`
- `samples/Game.Unity.Agar`
- actor, hotfix, source-generation, and starter documentation
- generator, actor runtime, hotfix unload, and sample business-logic tests

Strongly coupled work:

- method identity resolution, generated metadata, mailbox payload shape, remote
  envelope shape, and hotfix unload safety must stay under one implementation
  owner
- source generator output and runtime support types must change together

Independent follow-up slices:

- documentation rewrite after API shape compiles
- sample migration after generated API is available
- source-scan tests for removed API shapes
- package README example updates

Compatibility stance: breaking generated actor API compatibility is acceptable
because Lakona is still early and this removes a misleading public surface.
The old `Get(id)` and `Remote(nodeId, id)` business-facing generated APIs
should be removed rather than kept as compatibility aliases.

Versioning impact: any modified shippable package under `src/**` requires a
package version bump before release.

## API Shape

Generated actor collection type:

```csharp
public sealed class RoomActors
{
    public RoomLocalRef Local(RoomId id);

    public RoomRouteRef Route(RoomId id);
}
```

`Get(id)` is removed from the business-facing generated API.

`Remote(nodeId, id)` is removed from the business-facing generated API. The
runtime may keep a pinned node path internally, but generated project code and
ordinary hotfix business code must not address actor behavior by node id.

Local call:

```csharp
var result = await rooms.Local(roomId)
    .CallAsync(RoomBehavior.StartAsync, request, cancellationToken);
```

Routed call:

```csharp
var result = await rooms.Route(roomId)
    .CallAsync(RoomBehavior.LeaveAsync, request, cancellationToken);
```

`CallAsync` waits for the behavior method to complete. It supports both
`ValueTask<T>` behavior methods and `ValueTask` behavior methods.

`PostAsync` waits only for the actor message to be accepted for delivery. It
does not wait for the behavior method to execute. `PostAsync` supports only
`ValueTask` behavior methods.

Local post for void-returning behavior:

```csharp
await rooms.Local(roomId)
    .PostAsync(RoomBehavior.RunTickAsync, request, cancellationToken);
```

Routed post for void-returning behavior:

```csharp
await rooms.Route(roomId)
    .PostAsync(RoomBehavior.SubmitInputAsync, request, cancellationToken);
```

Same-actor direct behavior calls remain valid only when code is already running
inside that actor turn and has the actor instance:

```csharp
await self.CompleteAsync(request, cancellationToken);
```

Cross-actor calls must use a generated actor ref plus `CallAsync` or
`PostAsync`, even when the target actor is known to live in the same process.

## Selector Semantics

`Local(id)` means:

- dispatch only to the current process actor runtime
- enter the target actor mailbox
- do not query `ActorDirectory`
- do not use cluster transport
- fail with actor-call `ActorNotFound` when the current process does not host
  the actor

Use `Local(id)` only when the caller has already proven current-node ownership,
for example:

- a feature lifecycle hook immediately after `ActorHosting.CreateAsync`
- a timer callback created by an actor on the current node
- a battle-node realtime RPC that owns the local room runtime
- framework internals that already hold current-node ownership evidence

`Local(id)` is not a performance hint for ordinary business calls.

`Route(id)` means:

- resolve actor ownership by actor id
- use the local mailbox if the current process owns the actor
- otherwise send a remote actor envelope to the resolved owner
- use route cache invalidation on stale-route and location failures
- fail with structured actor-call exceptions when no route exists, the owner is
  unavailable, serialization fails, deserialization fails, or the call times out

`Route(id)` is the default cross-actor business call path.

## Method Identity

The call site passes the real hotfix behavior method:

```csharp
await users.Route(userId)
    .CallAsync(UserBehavior.LoginAsync, request, cancellationToken);
```

The method argument is an identity token, not the execution target.

Runtime and generated support code must obey these rules:

- The method group may be accepted at the public call boundary for type
  inference and IDE navigation.
- The method group must be resolved immediately to stable generated metadata:
  actor type, method id, method name, request type, and result type.
- The method group, delegate instance, `MethodInfo`, hotfix `Type`, hotfix
  object instance, or any other collectible-assembly reference must not be
  stored in actor envelopes, mailbox messages, timers, retries, diagnostics, or
  caches.
- Local and routed calls must execute through the current hotfix dispatch table,
  not by invoking the original delegate.
- Fire-and-forget `PostAsync` must resolve method identity before
  enqueueing the actor message.

This is the primary hotfix-unload safety invariant for the design. If the
implementation cannot prove this invariant with unload tests, the method-group
surface must be replaced by a generated non-delegate selector before release.

## Generated Code Boundary

The generator should stop emitting same-named extension wrappers on generated
actor refs:

```csharp
// Do not generate this anymore.
public static ValueTask<JoinRoomReply> JoinAsync(
    this RoomRouteRef self,
    JoinRoomRequest request,
    CancellationToken cancellationToken = default);
```

The generator should continue to emit:

- actor collection types such as `RoomActors`
- local and route ref structs
- method metadata for public behavior extension methods
- local call helpers that enter `IActorRuntime`
- route call helpers that use `ActorDirectory`, remote serialization, and
  `IRemoteActorInvoker`
- cluster handlers for remote actor envelopes
- DI registration for generated actor collections and handlers
- diagnostics metadata that does not retain hotfix assembly objects

Generated support APIs may use internal helper methods, but the business-facing
surface must make the actor call boundary explicit through `CallAsync` or
`PostAsync`.

## Failure Model

Business-facing calls throw typed actor call exceptions.

`Local(id)` failures describe current-process mailbox dispatch failures:

- actor not found locally
- actor type mismatch
- actor stopped or stopping
- local mailbox backpressure
- timeout
- cancellation
- handler unavailable in the current hotfix generation

`Route(id)` failures describe routed actor-call failures:

- route not found
- stale or expired route
- owner node unavailable
- remote handler unavailable
- remote backpressure
- timeout
- cancellation
- serialization failure
- deserialization failure
- response type mismatch

The exception details should include actor id, actor name, behavior method
name, method id, correlation id, status, and node when available. Default
diagnostics and metrics must continue to avoid high-cardinality tags such as
actor id and request values.

For `PostAsync`, these failures describe message acceptance failure. A
successful `PostAsync` means the local or remote actor runtime accepted the
message for delivery; it does not mean the behavior method has already run or
will report its later business failure to the caller.

## Agar Target Shape

Login service:

```csharp
await CreateUserActorOnStateStoreAsync(call.Services, account)
    .ConfigureAwait(false);

var login = await _users.Route(new UserId(account))
    .CallAsync(UserBehavior.LoginAsync, loginRequest, call.CancellationToken)
    .ConfigureAwait(false);

var snapshot = await _users.Route(new UserId(login.UserId))
    .CallAsync(
        UserBehavior.GetSnapshotAsync,
        new PlayerSessionSnapshotRequest(),
        call.CancellationToken)
    .ConfigureAwait(false);
```

Player service leaderboard query:

```csharp
var snapshot = await _leaderboards
    .Route(new LeaderboardId(AgarHotfixIds.GlobalLeaderboardActorId))
    .CallAsync(
        LeaderboardBehavior.GetLeaderboardAsync,
        new LeaderboardQueryRequest { TopN = topN },
        call.CancellationToken)
    .ConfigureAwait(false);
```

Player service room leave:

```csharp
return rooms.Route(new RoomId(snapshot.CurrentRoomId))
    .CallAsync(RoomBehavior.LeaveAsync, request, cancellationToken);
```

Battle service realtime input, where the KCP node owns the room locally:

```csharp
await _rooms.Local(new RoomId(req.RoomId))
    .PostAsync(RoomBehavior.SubmitInputAsync, inputRequest, call.CancellationToken)
    .ConfigureAwait(false);
```

Battle runtime feature after local actor creation:

```csharp
await _actorHosting
    .CreateAsync<RoomActor>(ActorId.From(payload.RoomId), call.CancellationToken)
    .ConfigureAwait(false);

var create = await _rooms.Local(new RoomId(payload.RoomId))
    .CallAsync(RoomBehavior.CreateAsync, createRequest, call.CancellationToken)
    .ConfigureAwait(false);

var start = await _rooms.Local(new RoomId(payload.RoomId))
    .CallAsync(RoomBehavior.StartAsync, startRequest, call.CancellationToken)
    .ConfigureAwait(false);
```

Battle runtime timer callback:

```csharp
await rooms.Local(new RoomId(tick.Args.RoomId))
    .PostAsync(
        RoomBehavior.RunTickAsync,
        new RoomTickRequest { ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime },
        tick.CancellationToken)
    .ConfigureAwait(false);
```

Room behavior same-actor settlement:

```csharp
await self.CompleteAsync(completionRequest, cancellationToken)
    .ConfigureAwait(false);
```

Room behavior calls to other actors:

```csharp
await users.Route(new UserId(userId))
    .CallAsync(UserBehavior.ClearRoomAsync, clearRoomRequest, cancellationToken)
    .ConfigureAwait(false);

await leaderboards.Route(new LeaderboardId(AgarHotfixIds.GlobalLeaderboardActorId))
    .CallAsync(
        LeaderboardBehavior.RecordVictoryPointsAsync,
        leaderboardRequest,
        cancellationToken)
    .ConfigureAwait(false);
```

Feature commands in Agar remain as they are in this phase. For example,
state-store user creation and battle-runtime room allocation still use the
existing feature command path until a separate design explicitly replaces it.

## Analyzer And Documentation Rules

Generated and sample code must not use:

```csharp
rooms.Get(roomId)
rooms.Remote(nodeId, roomId)
rooms.Route(roomId).JoinAsync(request)
rooms.Local(roomId).JoinAsync(request)
```

Generated and sample code should use:

```csharp
rooms.Route(roomId).CallAsync(RoomBehavior.JoinAsync, request, cancellationToken)
rooms.Local(roomId).CallAsync(RoomBehavior.StartAsync, request, cancellationToken)
rooms.Local(roomId).PostAsync(RoomBehavior.RunTickAsync, request, cancellationToken)
```

Docs must describe `Route` as the normal cross-actor path and `Local` as a
current-node ownership assertion. Existing language that calls generated refs
"local-looking behavior methods" should be removed.

Analyzer coverage should be considered for:

- blocking use of generated same-named actor ref wrappers after the migration
- warning when generated project code uses raw `IActorRuntime.AskAsync` or
  `TellAsync` for business actor behavior
- warning when project hotfix code uses a pinned node actor call surface if an
  advanced internal API remains visible
- enforcing supported `CallAsync` behavior method shapes

## Migration Plan

Milestone 1: generated API shape.

- Add `Route(id)` generated refs.
- Keep `Local(id)` generated refs.
- Stop emitting `Get(id)`.
- Stop emitting business-facing `Remote(nodeId, id)`.
- Stop emitting same-named behavior wrapper methods.
- Emit `CallAsync` and `PostAsync` support.

Milestone 2: runtime and hotfix safety.

- Resolve method-group arguments to generated metadata immediately.
- Ensure actor messages and remote envelopes contain stable ids and payloads,
  not delegates or reflection objects from the hotfix assembly.
- Add unload tests that fail if actor calls, posts, or failed dispatches retain
  the old hotfix assembly.

Milestone 3: sample and template migration.

- Migrate `samples/Game.Unity.Agar` to `Local` / `Route` / `CallAsync` /
  `PostAsync`.
- Migrate generated starter templates.
- Update docs and README examples.
- Add source-scan tests for removed API shapes.

Milestone 4: cleanup.

- Remove stale docs that recommend `Get(id)` or `Remote(nodeId, id)`.
- Review generated API names for public consistency.
- Apply package version bumps for modified shippable packages.

## Validation

Focused validation should include:

- `dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj`
- `dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj`
- `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`
- `dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
- source scans for `Get(`, `.Remote(new NodeId`, generated same-named actor ref
  calls, and raw business `AskAsync` / `TellAsync`
- `pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1` before
  release-impacting completion

Full solution validation remains:

```powershell
dotnet build Lakona.slnx
dotnet test Lakona.slnx --no-build
```

Network-restricted environments should request external access for restore on
the first .NET command that may contact NuGet, then use `--no-restore` or
`--no-build` where possible.

## Open Questions

1. Should the public route ref type be named `RoomRouteRef` or keep a shorter
   generated name such as `RoomRef` while the selector method is `Route(id)`?
2. Should an advanced pinned-node actor-call API exist at all outside tests and
   internal runtime code?
3. Should method-group `CallAsync` be the final public surface, or should the
   generator emit explicit selector values to avoid any delegate creation at
   the call boundary?
