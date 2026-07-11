# Network-Transition Reconnect Implementation Plan

## Status

Ready for implementation. The approved design is
`docs/superpowers/specs/2026-07-11-network-transition-reconnect-design.md`.

## Goal

Make a short Wi-Fi-to-cellular-style network transition seamless for Agar:
both WebSocket and KCP RPC sessions are replaced, the existing control and
realtime Game Sessions are resumed, reliable control callbacks published while
offline replay in strict order, and best-effort KCP world state resumes from a
fresh snapshot without replaying historical frames.

The public resume window is `Lakona:Sessions:ResumeWindowSeconds`, defaults to
60 seconds, and is negotiated to the client during handshake.

## Scope Checkpoint

This is a large cross-cutting change. It affects public configuration, wire
contracts, server session lifecycle, reliable-push concurrency, client sequence
handling, source-generated clients, starter templates, Agar business state,
Unity connection state, acceptance tests, documentation, and package versions.

One continuity-preserving implementation owner must complete milestones 1-6.
In particular, endpoint policy capture, Game Session retention, route renewal,
outbox sequencing, replay barriers, acknowledgement, handshake, and client gap
handling must not be split across independent owners. After runtime behavior is
stable, documentation scans and final checklist verification are safe
independent review tasks.

Compatibility is deliberately breaking for configuration: remove the global
`Lakona:ReliablePush:Enabled`, remove independent reliable-push/session
retention settings, default endpoint reliable push to false, and require an
explicit endpoint `"ReliablePush": true` opt-in. Do not add compatibility
aliases or silent fallback behavior.

## Milestone 1: Lock The Public Contract With Failing Tests

### Task 1.1: Add the negotiated resume-window contract

Files:

- Modify `src/Lakona.Game.Abstractions/Sessions/GameHandshake.cs`.
- Modify `src/Lakona.Game.Abstractions/Internal/LakonaInternalCodec.cs`.
- Modify `tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj`
  only if a new test dependency is required.
- Add or modify the focused handshake codec test under
  `tests/Lakona.Game.Abstractions.Tests`.

Steps:

1. Add a failing round-trip test for
   `GameServerHello.SessionResume.Window`, including a 60-second value.
2. Add the smallest public handshake DTO shape that represents the negotiated
   window without exposing server cleanup details.
3. Extend the internal codec and make the test pass.
4. Run:

   ```powershell
   dotnet test tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj
   ```

Review gate: confirm the wire shape describes a duration and has a deterministic
default for older or malformed payloads. Do not add retry counts to the wire
contract.

### Task 1.2: Replace public server configuration surfaces

Files:

- Modify `src/Lakona.Game.Server/Configuration/LakonaGameEndpointOptions.cs`.
- Modify `src/Lakona.Game.Server/Configuration/LakonaGameHostingOptions.cs`.
- Modify `src/Lakona.Game.Server/ReliablePush/ReliablePushOptions.cs`.
- Modify configuration readers in `src/Lakona.Game.Server/Configuration`.
- Modify endpoint resolution and guardrail DTOs/rules under
  `src/Lakona.Game.Server/Guardrails` as required.
- Add or modify focused tests under `tests/Lakona.Game.Server.Tests/Hosting` and
  `tests/Lakona.Game.Server.Tests`.

Steps:

1. Add failing tests proving:
   - omitted endpoint `ReliablePush` resolves to false;
   - only explicit true resolves to true;
   - `Sessions.ResumeWindowSeconds` defaults to 60;
   - invalid non-positive resume windows are rejected;
   - the removed global enabled and independent retention values are not used.
2. Add the optional endpoint property and the single session resume-window
   option.
3. Remove the global enabled flag, `ReliablePush.Retention`, and
   `Sessions.Cleanup.DisconnectedRetentionSeconds` from runtime option models
   and configuration binding.
4. Keep cleanup interval as an operational setting that cannot extend resume
   eligibility.
5. Run:

   ```powershell
   dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj
   ```

Review gate: scan the server source for all removed setting names before
continuing.

## Milestone 2: Make Session Policy And Deadline Durable Across Disconnect

### Task 2.1: Capture endpoint policy at connection and Game Session creation

Files:

- Modify `src/Lakona.Game.Server/Hosting/LakonaEndpointRpcServerConfigurator.cs`.
- Modify `src/Lakona.Game.Server/Hosting/LakonaGameEndpointCatalog.cs`.
- Modify session models and snapshots under `src/Lakona.Game.Server/Sessions`.
- Modify `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs`.

Steps:

1. Add failing tests proving the accepted endpoint's effective reliable-push
   policy is captured during connection-bound Game Session creation and remains
   readable after RPC disconnect.
2. Add internal connection policy state and clean it up when the RPC session
   ends.
3. Add retained Game Session delivery policy. Advanced unbound creation must
   capture best effort.
4. Reject callback binding or resume when connection and retained Game Session
   policies differ, without mutating the old binding or outbox.
5. Run the focused registry and lifecycle tests through the server test project.

Review gate: verify no business-facing session item stores a transport,
endpoint name, callback object, or RPC session.

### Task 2.2: Enforce an exact resume deadline

Files:

- Modify `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`.
- Modify `src/Lakona.Game.Server/Sessions/GameSessionResumeService.cs`.
- Modify `src/Lakona.Game.Server/Sessions/GameSessionCleanupHostedService.cs`.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionResumeServiceTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionCleanupHostedServiceTests.cs`.

Steps:

1. Add clock-controlled failing tests for resume immediately before, exactly at,
   and after `ResumeDeadlineUtc`.
2. Capture the exact deadline when the last bound RPC connection disconnects.
3. Make `TryResume` compare directly against the deadline; cleanup cadence must
   never extend it.
4. Clear or replace the deadline atomically on successful rebind and set a new
   one on the next disconnect.
5. Preserve the same `GameSessionKey`, including generation, on successful
   resume.
6. Run the server test project.

Review gate: specifically inspect disconnect/resume races and ensure an expired
session cannot be resurrected by a late cleanup scan.

### Task 2.3: Align client-session route lifetime with the resume window

Files:

- Modify `src/Lakona.Game.Server/Sessions/ClientSessionRouteRegistrar.cs`.
- Modify `src/Lakona.Game.Server/Sessions/ClientSessionRouteLifecycleHandler.cs`.
- Modify `src/Lakona.Game.Server/Sessions/SessionServiceCollectionExtensions.cs`.
- Modify `src/Lakona.Game.Server/Sessions/GameHeartbeatService.cs`.
- Modify related route and heartbeat tests under
  `tests/Lakona.Game.Server.Tests`.

Steps:

1. Add failing tests proving each successful heartbeat renews the client-session
   route through at least the complete resume window after that heartbeat.
2. Stop deriving this route's recovery lifetime from cluster
   `RouteLeaseSeconds`.
3. Renew on heartbeat; keep the route across ordinary RPC disconnect; remove it
   on Game Session expiration or termination.
4. Verify offline notification intent still reaches the owner outbox throughout
   the resumable interval.
5. Run `GameHeartbeatTests` and the complete server test project.

## Milestone 3: Make Reliable Push Strict, Per-Session, And Honest

### Task 3.1: Replace silent overflow with continuity loss

Files:

- Modify `src/Lakona.Game.Server/ReliablePush/IReliablePushOutbox.cs`.
- Modify `src/Lakona.Game.Server/ReliablePush/InMemoryReliablePushOutbox.cs`.
- Modify `src/Lakona.Game.Server/ReliablePush/ReliablePushRuntime.cs`.
- Modify reliable continuity state under `src/Lakona.Game.Server/Sessions` if
  it belongs to the Game Session snapshot.
- Modify `tests/Lakona.Game.Server.Tests/ReliablePushOutboxTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/ReliablePushAckServiceTests.cs`.

Steps:

1. Add failing tests showing the 257th unacknowledged notification at the
   default capacity 256 does not evict sequence 1.
2. Atomically mark that Game Session generation `StateRefreshRequired`, emit a
   low-cardinality diagnostic, and stop normal replay.
3. Preserve `MaxPendingPerSession` as the only public reliable-push resource
   setting.
4. Ensure best-effort Game Sessions allocate no pending records.
5. Run the server test project.

### Task 3.2: Serialize append, replay, live send, and acknowledgement per session

Files:

- Modify `src/Lakona.Game.Server/ReliablePush/InMemoryReliablePushOutbox.cs`.
- Modify `src/Lakona.Game.Server/ReliablePush/ReliablePushRuntime.cs`.
- Modify `src/Lakona.Game.Server/ReliablePush/ReliablePushAckService.cs`.
- Modify `src/Lakona.Game.Server/Sessions/ClientNotificationOwnerDispatcher.cs`.
- Modify `src/Lakona.Game.Server/Sessions/ClientNotificationCommandRouter.cs`.
- Modify reliable-push tests under `tests/Lakona.Game.Server.Tests`.

Steps:

1. Add deterministic concurrency tests for:
   - publications arriving while replay is in progress;
   - acknowledgement racing replay;
   - rebind racing publication;
   - duplicate acknowledgements;
   - session-policy mismatch.
2. Introduce a per-Game-Session serialization boundary and explicit replay/live
   state. Avoid a process-wide delivery lock.
3. Append and assign sequence before attempting delivery. While replaying, new
   records remain behind the barrier.
4. Transition to live only after delivery catches up to the current tail.
5. Admit acknowledgements only when both connection and Game Session policies
   are reliable and the acknowledgement advances a valid contiguous prefix.
6. Run the server test project repeatedly enough to expose race-test flakes.

Review gate: architecture and concurrency review. Confirm business publishers
still call only `IClientNotifications.NotifyAsync` and have no online/cache/retry
branches.

### Task 3.3: Make handshake and RPC endpoints policy-aware

Files:

- Modify `src/Lakona.Game.Server/Sessions/GameHandshakeService.cs`.
- Modify `src/Lakona.Game.Server/Hosting/LakonaEndpointRpcServerConfigurator.cs`.
- Modify `src/Lakona.Game.Server/ReliablePush/ReliablePushAckDecider.cs` as
  required.
- Modify `tests/Lakona.Game.Server.Tests/GameHandshakeTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/GameHandshakeGateTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/ReliablePushAckRpcTests.cs`.

Steps:

1. Add failing tests for reliable and best-effort endpoint hello values and the
   negotiated 60-second default window.
2. Populate hello from the accepted connection's endpoint policy, not a global
   option.
3. Reject reliable acknowledgements on best-effort connections or mismatched
   Game Sessions deterministically.
4. Run the server test project.

## Milestone 4: Enforce Contiguous Client Application And Expose The Deadline

### Task 4.1: Reject reliable-push gaps before business application

Files:

- Modify `src/Lakona.Game.Client/ReliablePush/ReliablePushTracker.cs`.
- Modify `src/Lakona.Game.Client/ReliablePush/ReliablePushInbox.cs`.
- Modify `src/Lakona.Game.Client/Runtime/Sessions/ClientSessionController.cs`.
- Modify `src/Lakona.Game.Client/LakonaGameClientCore.cs`.
- Modify `tests/Lakona.Game.Client.Tests/ReliablePushTrackerTests.cs`.
- Modify `tests/Lakona.Game.Client.Tests/ReliablePushInboxTests.cs`.
- Modify `tests/Lakona.Game.Client.Tests/ClientSessionControllerTests.cs`.

Steps:

1. Add failing tests for exact next, duplicate, and gap sequences.
2. Apply and persist only `lastApplied + 1`; acknowledge duplicates without
   applying; reject a gap without applying or acknowledging it.
3. Surface a gap as `StateRefreshRequired` and prevent later callbacks in that
   broken generation from masquerading as a complete stream.
4. Run:

   ```powershell
   dotnet test tests/Lakona.Game.Client.Tests/Lakona.Game.Client.Tests.csproj
   ```

### Task 4.2: Apply and expose the negotiated resume window

Files:

- Modify `src/Lakona.Game.Client/LakonaGameClientCore.cs`.
- Modify `src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs`.
- Modify `tests/Lakona.Game.Client.Tests` handshake tests.
- Modify `tests/Lakona.Rpc.Analyzers.Tests/LakonaRpcSourceGeneratorTests.cs`.

Steps:

1. Add failing tests proving `ApplyServerHello` stores the negotiated window and
   the generated `LakonaGameClient` exposes it.
2. Add the smallest engine-neutral client property needed by Agar to compute an
   absolute reconnect deadline.
3. Update generated source and source-shape assertions.
4. Run the client and analyzer test projects sequentially.

Review gate: public API review. The framework exposes negotiated policy and
outcomes, not an Agar-specific recovery coordinator.

## Milestone 5: Update Starter Output And Configuration Consumers

### Task 5.1: Migrate generated application configuration

Files:

- Modify `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`.
- Modify `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`.
- Modify any current starter snapshots or fixtures referenced by those tests.

Steps:

1. Add failing source-shape tests that generated business endpoints explicitly
   contain `"ReliablePush": true`, including KCP variants.
2. Remove generated global enabled and independent retention settings.
3. Emit `Sessions.ResumeWindowSeconds` only where the template intentionally
   demonstrates overriding the 60-second default; otherwise rely on the
   documented default.
4. Run:

   ```powershell
   dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj
   ```

### Task 5.2: Add repository-wide stale configuration guards

Files:

- Modify existing repository guards under `tests/Lakona.RepositoryGuards.Tests`
  if current guards do not cover configuration samples.

Steps:

1. Add a guard that fails when active configuration or template output uses
   `Lakona:ReliablePush:Enabled`, `ReliablePush.Retention`, or
   `DisconnectedRetentionSeconds`.
2. Exclude the approved design/plan historical explanation only when necessary
   and narrowly scoped.
3. Run the repository guard test project.

## Milestone 6: Migrate Agar Business And Server Lifecycle

### Task 6.1: Configure control reliable and realtime best effort

Files:

- Modify `samples/Game.Unity.Agar/Server/App/appsettings.json`.
- Modify `samples/Game.Unity.Agar/docker-compose.yml` only if it overrides the
  affected settings.
- Modify Agar configuration tests or business tests as appropriate.

Steps:

1. Explicitly set WebSocket `ReliablePush` true and KCP `ReliablePush` false.
2. Set `Lakona:Sessions:ResumeWindowSeconds` to 60 only if the sample benefits
   from showing the public setting; otherwise use the framework default.
3. Remove old global and retention settings.
4. Confirm the resolved handshake is reliable for WS and best effort for KCP.

### Task 6.2: Add real match-progress control callbacks

Files:

- Modify `samples/Game.Unity.Agar/Shared/Interfaces/IPlayerService.cs`.
- Add the MemoryPack-compatible progress DTO under
  `samples/Game.Unity.Agar/Shared/State` or the existing matching contracts file.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/RoomNotifier.cs`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`.
- Modify focused tests under
  `samples/Game.Unity.Agar/tests/BusinessLogic.Tests`.

Steps:

1. Add failing contract/business tests for a once-per-second
   `OnMatchProgress(MatchProgressUpdate)` publication with monotonic
   `ProgressRevision`, authoritative tick, remaining seconds, and
   `PublishedAtUtc`.
2. Store the control `GameSessionKey` as stable player identity where needed;
   do not store callbacks, RPC sessions, transports, or services in actor state.
3. Publish unconditionally through `IClientNotifications`; do not inspect
   connection availability or retry in business code.
4. Run the Agar business test project.

### Task 6.3: Resume the existing realtime Game Session

Files:

- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/AgarSessionLifecycle.cs`.
- Modify room/player session contracts under
  `samples/Game.Unity.Agar/Shared/State`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarSessionLifecycleTests.cs`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarRealtimeSessionItemTests.cs`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/PlayerSessionActorStateTests.cs`.

Steps:

1. Add failing tests proving ordinary KCP disconnect retains realtime identity
   and a validated attach resumes the same `GameSessionKey`.
2. Validate replacement attach through the resumed control session and the same
   player, room, and match.
3. Clear realtime identity only on expiration, termination, or explicit
   supersession policy.
4. If realtime resume returns `StateLost` after control recovery, create a
   replacement realtime Game Session only through the approved validated path.
5. Keep KCP callbacks best effort and send a fresh world state after attach.
6. Run the Agar business tests, then the server tests.

Review gate: confirm the normal acceptance path keeps both Game Session keys,
while both RPC connection ids change.

## Milestone 7: Add The Unity Recovery State Machine And Fault Gate

### Task 7.1: Coordinate dual-channel recovery in Agar

Files:

- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaNetworkSession.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaMultiplayerState.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaMultiplayerFlow.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Session.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Callbacks.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Testing.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaCallbackInbox.cs`.

Steps:

1. Model one recovery operation for simultaneous WS/KCP loss. Preserve the
   Player Session and Game Session identities while discarding both old RPC
   sessions.
2. Reconnect control first, using the absolute deadline derived from the last
   negotiated resume window; do not use a fixed attempt count.
3. After control resume and player/room/match validation, resume realtime and
   wait for a fresh world state before returning to live gameplay.
4. Deduplicate disconnect callbacks and ensure cancellation, logout,
   unauthorized responses, `StateLost`, and `StateRefreshRequired` terminate
   recovery explicitly.
5. Record progress revisions for acceptance without changing ordinary business
   callback handling.
6. Apply the `unity-network-ui-state` skill during implementation to review
   ownership, stale completion guards, and UI-state transitions.

### Task 7.2: Add a test-only network-stack gate

Files:

- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaNetworkSession.cs`.
- Modify `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Testing.cs`.

Steps:

1. Add a test-only gate that, when closed, rejects new connection attempts and
   terminates both real transport connections through their normal disconnect
   paths.
2. Opening the gate only permits future attempts; it must not invoke reconnect
   directly.
3. Expose timestamps and old/new RPC connection ids needed by PlayMode
   assertions without exposing production-only fault controls in framework
   packages.
4. Compile the Unity client and inspect the Console for new errors before
   running PlayMode acceptance.

## Milestone 8: Prove Offline Replay And Fresh KCP State End To End

### Task 8.1: Extend the three-node PlayMode smoke test

Files:

- Modify
  `samples/Game.Unity.Agar/Client/Assets/Tests/PlayMode/DotArenaThreeNodePlayModeTests.cs`.
- Modify `scripts/game/ci/test-agar-three-node.ps1` only if new evidence must be
  collected or surfaced.
- Modify `samples/Game.Unity.Agar/README.md` for the manual reproduction path.

Steps:

1. Reach an active match and wait until progress callbacks and KCP world states
   are both observed.
2. Record player, room, match, both Game Session keys, both RPC connection ids,
   last progress revision, last reliable sequence, and last world tick.
3. Close the network gate for 3 seconds, then open it.
4. Wait for normal recovery and assert:
   - both RPC connection ids changed;
   - both Game Session keys are unchanged on the normal path;
   - player, room, and match are unchanged;
   - at least two replayed progress updates have `PublishedAtUtc` inside the
     offline interval, allowing only a small scheduling tolerance;
   - progress revisions are contiguous and each is applied once;
   - live progress continues contiguously after replay;
   - KCP handshake advertises reliable push false;
   - the first recovered world state is fresh and historical KCP frames were
     not replayed;
   - movement input and authoritative movement resume.
5. Log the phase, offline interval, identities, connection ids, progress
   revisions/timestamps, reliable sequence, world ticks, and visible UI state
   on failure.
6. Run:

   ```powershell
   pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
   ```

Review gate: treat any `StateLost`/`StateRefreshRequired` on the normal 3-second
path as a failed seamless-recovery test. Do not hide it with a fresh login.

### Task 8.2: Add focused negative coverage

Files:

- Modify the most focused server, client, Agar business, and/or PlayMode test
  files already touched above.

Steps:

1. Prove resume after the negotiated deadline returns `StateLost`.
2. Prove owner restart or missing process-local state returns `StateLost` and
   does not claim complete replay.
3. Prove wrong gateway/no affinity returns `StateLost`; do not redirect.
4. Prove overflow and client sequence gap return `StateRefreshRequired` and do
   not apply a partial stream.
5. Prove best-effort sessions never replay pending history.

## Milestone 9: Documentation, Versions, And Final Integration

### Task 9.1: Update durable documentation

Files:

- Modify `docs/configuration.md`.
- Modify `docs/session.md`.
- Modify `docs/guardrails.md` if validation behavior is user-visible.
- Modify `src/Lakona.Game.Abstractions/README.md`.
- Modify `src/Lakona.Game.Client/README.md` if present and affected.
- Modify `src/Lakona.Game.Server/README.md`.
- Modify `src/Lakona.Tool/README.md`.
- Modify `samples/Game.Unity.Agar/README.md`.
- Modify `CONTEXT.md` only if implementation changes approved terminology.

Steps:

1. Document the 60-second default, negotiated deadline, endpoint-only opt-in,
   capacity failure semantics, ordering contract, affinity requirement, and
   process-local `StateLost` boundary.
2. Remove stale examples of global enable and independent retention settings.
3. Keep the approved spec and plan until implementation review is complete;
   then follow repository policy for retiring temporary planning documents once
   durable docs carry the contract.
4. Run:

   ```powershell
   pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
   ```

### Task 9.2: Bump the complete package dependency closure

Files:

- Modify package versions in the affected `.csproj` files under `src/**`.
- Modify any dependency versions required by the package graph.

Steps:

1. Bump at least `Lakona.Game.Abstractions`, `Lakona.Game.Client`,
   `Lakona.Game.Server`, `Lakona.Rpc.Analyzers`, and `Lakona.Tool`.
2. Run the repository package-version graph guard and bump every additional
   shippable package required by the dependency closure.
3. Do not publish packages as part of this implementation unless separately
   authorized.

### Task 9.3: Final sequential validation and hygiene

Run affected suites sequentially because they share build outputs and global
state:

```powershell
dotnet test tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj
dotnet test tests/Lakona.Game.Client.Tests/Lakona.Game.Client.Tests.csproj
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj
dotnet test tests/Lakona.RepositoryGuards.Tests/Lakona.RepositoryGuards.Tests.csproj
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
git diff --check
```

Also run the repository's package-version graph guard named by the current
contributor documentation. Record every skipped command with its exact reason
and residual risk.

Final review must compare the complete diff with the approved design and focus
on:

- exact deadline enforcement;
- disconnect/resume/expiration races;
- replay/live publication ordering;
- gap and overflow honesty;
- endpoint/Game Session policy mismatch;
- absence of business-layer reconnect branches;
- same Game Session keys with new RPC connection ids;
- no accidental distributed-state or redirect behavior;
- no stale configuration names or unbumped packages.

## Completion Criteria

The work is complete only when the 3-second dual-transport interruption passes
the three-node acceptance test with offline control callbacks replayed exactly
once and in order, fresh best-effort KCP state resumes, both old RPC sessions
are replaced, both normal-path Game Session keys remain unchanged, durable docs
match the shipped behavior, affected package versions are consistent, and all
required validation is green on the final integrated branch.
