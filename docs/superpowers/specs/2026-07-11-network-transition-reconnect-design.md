# Network-Transition Reconnect And Endpoint Reliable Push Design

## Status

Proposed for review. This document defines the intended contract before any
runtime or sample implementation begins.

## Goal

Make a common client network transition, such as moving from Wi-Fi to cellular,
recover without game-specific offline branches:

- the WebSocket and KCP transport connections are both lost;
- both old RPC sessions end permanently and are never reused;
- the control and realtime game sessions remain resumable during their retention
  window;
- new transport connections create new RPC sessions and rebind the existing game
  sessions;
- reliable control notifications published while the client is offline are
  replayed after control-session resume;
- realtime KCP notifications are best effort when the KCP endpoint opts out of
  reliable push, so historical world-state frames are not replayed;
- Agar returns to the same player, room, and match and can continue receiving
  world state and submitting input.

The framework must own connection availability, sequencing, acknowledgement,
pending storage, and replay. Business publishers continue to publish notification
intent through `IClientNotifications` without checking whether a client is online,
caching messages, or implementing retry loops.

## Current State At The Design Baseline

This design is based on repository `main` at `d695e5ff`.

The current implementation already provides the following foundations:

- a game session is distinct from an RPC connection and can remain after its
  connection disconnects;
- the control login path can resume the existing control `GameSessionKey` onto a
  new RPC connection;
- reliable push is sequenced and retained only by the current owner of the
  `GameSessionKey` route;
- remote business nodes relay unsequenced notification intent to that owner;
- a client-session route remains registered across RPC disconnect and is removed
  on game-session expiration or termination, allowing offline notification
  intent to keep reaching the owner outbox;
- the client reliable-push inbox deduplicates sequences and acknowledges applied
  notifications;
- Agar uses separate control and realtime game sessions.

The current implementation does not yet satisfy this design:

- `Lakona:ReliablePush:Enabled` is process-wide;
- every endpoint advertises the same reliable-push setting during handshake;
- `ReliablePushRuntime` applies the same policy to every game session;
- a game session does not retain the effective delivery policy of the endpoint
  on which it was first bound;
- Agar clears realtime actor state as soon as the KCP RPC session disconnects;
- Agar's next KCP attach creates a new game session rather than resuming the
  existing realtime game session;
- Agar has no frequent control-plane callback that deterministically proves
  offline reliable-push replay;
- the current Unity smoke test covers connection and gameplay but does not hold
  both transports offline and verify recovery.

## Terminology And Invariants

### Transport connection

A WebSocket or KCP connection owned by the RPC runtime. A network transition
destroys it.

### RPC session

The connection-scoped RPC context, callback proxy, and connection id. It has the
same lifetime as its transport connection. Once disconnected, it is dead and
must never be rebound or reused.

### Game session

The framework-owned resumable identity represented by `GameSessionKey`. It owns
game-session items, callback binding state, reliable-push identity, and retention
semantics. A new RPC session may bind an existing game session after successful
resume validation.

### Required invariants

1. A reconnect always has a new RPC connection id.
2. A successful resume keeps the same `GameSessionKey`, including generation.
3. A callback binding references only the current RPC session.
4. Rebinding removes the old connection-to-session association atomically.
5. Reliable-push sequence ownership remains on the game-session route owner.
6. Endpoint reliability is captured as game-session delivery policy and remains
   available while the RPC connection is absent.
7. Business notification code does not branch on connection availability.
8. A realtime disconnect does not clear room/user realtime identity until the
   game session expires, is terminated, or is superseded by product policy.

## Scope Checkpoint

### Classification

This is a large cross-cutting change. It changes public endpoint configuration,
session lifecycle, reliable-push dispatch, handshake behavior, Agar contracts,
Agar actor state, Unity connection recovery, and three-node acceptance coverage.

### Affected surfaces

- `Lakona.Game.Server`
  - endpoint configuration and validation;
  - per-connection endpoint policy capture;
  - per-game-session delivery policy retention;
  - handshake, publication, acknowledgement, and replay decisions;
  - session disconnect, resume, expiration, and termination cleanup.
- Framework documentation
  - `docs/configuration.md`;
  - `docs/session.md`;
  - `docs/guardrails.md` if endpoint validation changes are externally visible;
  - package README examples that show endpoint or reliable-push configuration.
- `Game.Unity.Agar`
  - WebSocket and KCP endpoint configuration;
  - shared callback contracts and DTOs;
  - control progress publication;
  - realtime game-session resume;
  - disconnect/expiration lifecycle behavior;
  - Unity reconnect coordination and test-only network fault injection;
  - business tests, PlayMode acceptance, and sample documentation.
- `Lakona.Tool`
  - only if generated endpoint configuration or source-shape guards explicitly
    emit or reject the new endpoint property. The default remains backward
    compatible when no endpoint override is emitted.

### Coupling assessment

Endpoint policy resolution, game-session policy retention, owner-side reliable
push, handshake, and session resume are strongly coupled and require one
continuity-preserving implementation owner. Agar's realtime resume must be
implemented against that final framework contract by the same owner or as the
immediately following milestone.

Documentation wording checks, stale-name scans, and final checklist verification
are independent only after runtime behavior is stable.

### Compatibility stance

Existing configuration remains valid:

- the global `Lakona:ReliablePush:Enabled` setting remains the default;
- endpoints without an override inherit the global value;
- no wire-protocol shape changes are required because `GameServerHello` already
  carries effective reliable-push settings;
- existing business notification calls remain unchanged.

Adding a nullable endpoint override is additive. Internal session and delivery
policy APIs may be redesigned cleanly. If a public framework API must change,
the old ambiguous surface should be removed rather than preserved through a
compatibility shim, consistent with repository policy.

### Versioning impact

Any shippable change under `src/Lakona.Game.Server/**` requires a
`Lakona.Game.Server` package version bump. `Lakona.Tool` requires a bump only if
its shippable source or templates change. Other packages require bumps only if
implementation proves that their shipped contracts must change.

## Endpoint Reliable-Push Configuration

Each client-facing endpoint gains an optional boolean override:

```json
{
  "Lakona": {
    "ReliablePush": {
      "Enabled": true
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "ReliablePush": true,
        "RpcServices": [ "login", "player" ]
      },
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20001,
        "ReliablePush": false,
        "RpcServices": [ "battle" ]
      }
    ]
  }
}
```

The effective value is:

```text
endpoint.ReliablePush ?? Lakona.ReliablePush.Enabled
```

The global default remains `true`. Omitting the endpoint property preserves
current behavior.

The property configures framework notification delivery, not transport
reliability. KCP may still retransmit packets within one live KCP connection.
Setting endpoint reliable push to `false` means callback commands are not placed
in the game-session outbox for replay across a later RPC session.

## Delivery Policy Ownership

### Connection policy

Every accepted client RPC connection is associated internally with the
effective reliable-push policy of its endpoint. The endpoint configurator has
the authoritative configuration and must make the same effective value
available to:

- the framework handshake response;
- game-session binding;
- acknowledgement admission;
- diagnostics and validation.

Connection-policy state is connection-scoped and is removed when the RPC session
ends.

### Game-session policy

When a connection-bound game session is created, it captures the effective
endpoint policy as part of the creation-and-first-binding transaction. That
policy is retained independently of the live callback and RPC connection so
publication can be decided while the client is offline.

Resume onto an endpoint with a different effective policy is rejected as a
session-policy mismatch. A game session must not silently change from reliable
to best effort, or the reverse, because pending sequences and client cursor
semantics would become ambiguous.

Sessions created through advanced unbound APIs capture the global default at
creation. Their later callback binding must match that fixed policy. The
implementation should keep this policy internal rather than exposing transport
or endpoint names through business session state.

Policy state is removed on game-session expiration or termination, not on RPC
disconnect.

### Owner-side publication

The game-session route owner remains authoritative:

```text
remote business node
  -> unsequenced notification intent
  -> GameSessionKey route owner
  -> read retained game-session delivery policy
  -> reliable: append to owner outbox, assign sequence, attempt callback send
  -> best effort: attempt callback send without outbox or reliable metadata
```

If no route owner accepts the notification, no outbox record exists and the
existing `RouteNotFound` semantics continue to apply.

### Handshake, acknowledgement, and replay

- `GameServerHello.ReliablePush` reports the endpoint's effective policy.
- A reliable endpoint advertises `Enabled=true` and `AckRequired=true`.
- A best-effort endpoint advertises both values as false.
- Replay runs only for game sessions whose retained policy is reliable.
- Best-effort sessions never create reliable metadata or pending records.
- Reliable-push acknowledgements are accepted only when both the connection and
  its bound game session use reliable policy.
- A mismatched connection/session policy fails deterministically and does not
  mutate the outbox.

## Agar Business Contract

### Endpoint policy

- WebSocket control endpoint: `ReliablePush=true`.
- KCP battle endpoint: `ReliablePush=false`.

### Control progress callback

Add a control-plane callback method to the existing `IPlayerCallback`; do not
create a new RPC service solely for testing:

```csharp
[RpcNotification(2)]
void OnMatchProgress(MatchProgressUpdate update);
```

`MatchProgressUpdate` contains at least:

- `MatchId`;
- `RoomId`;
- authoritative `ServerTick`;
- `RoundRemainingSeconds`;
- a per-match monotonic `ProgressRevision`.

During an active match, the battle runtime publishes one update per second to
each player's control `GameSessionKey`. The publisher always calls
`IClientNotifications`; it does not inspect callback availability or retry
failed sends. The route owner decides whether to send immediately or retain the
record.

The room's stable player state retains the control game-session id and
generation required to target this callback. It must store only stable identity,
not callback objects, RPC sessions, transports, or DI services.

The callback is a real sample capability, not a test-only pulse. The Unity
client may use the latest progress value for presentation, while its test
surface retains received revisions so acceptance can verify replay ordering and
deduplication.

### Realtime game-session resume

Initial battle attach creates the realtime game session. A later attach after a
transport loss resumes the same `GameSessionKey`:

```text
initial KCP attach
  -> create realtime GameSessionKey
  -> bind IBattleCallback
  -> retain id and generation in user/room stable state

KCP RPC disconnect
  -> clear live callback and connection association
  -> retain realtime GameSessionKey and room/user association

new KCP RPC connection
  -> authenticate player, room, and match
  -> resume retained realtime GameSessionKey
  -> bind new IBattleCallback
  -> confirm same id and generation to the client
```

The attach contract may carry explicit reconnect intent, but the server remains
authoritative. It resumes only when the authenticated player, retained realtime
game session, room, and match all agree. Initial attach creates a new game
session only when no resumable realtime session exists.

Realtime identity is cleared when:

- the realtime game session expires after the configured disconnected retention
  window;
- it is explicitly terminated;
- the room or match ends;
- product policy deliberately supersedes it.

It is not cleared merely because its RPC session disconnected.

## Unity Recovery State Machine

A simultaneous network transition is one recovery episode even though two
transport callbacks may arrive in either order.

```text
Connected
  -> NetworkUnavailable
  -> ReconnectingControl
  -> ReconnectingRealtime
  -> AwaitingFreshWorldState
  -> Connected
```

Rules:

1. The first transport-loss callback starts the episode; later callbacks join
   it and must not start competing retry loops.
2. Transport callbacks only enqueue state changes. Unity presentation changes
   occur on the main thread.
3. UI enters a visible pending state immediately and blocks duplicate network
   actions.
4. Control reconnect creates a new WebSocket RPC session and resumes the
   existing control game session.
5. Realtime reconnect creates a new KCP RPC session and resumes the existing
   realtime game session.
6. Gameplay input remains disabled until KCP attach succeeds and at least one
   fresh world state is applied.
7. Reliable control callbacks replay automatically after control resume.
8. Historical KCP world states are not replayed because the KCP endpoint is
   best effort; the client continues from the first new authoritative state.
9. A state-lost or terminated decision exits recovery through the existing
   explicit reset path rather than silently creating a replacement game session
   inside the same match.

Retry timing remains bounded and cancellation-safe. The design does not require
an infinite retry loop; the initial acceptance window only needs to survive a
short network transition inside the game-session retention period.

## Deterministic Network Fault Injection

The primary acceptance test uses a Unity test-only network availability gate at
the `DotArenaNetworkSession` boundary.

When the gate closes it must:

- reject subsequent WebSocket and KCP connection attempts;
- end both active transport connections so the server observes real RPC-session
  disconnect lifecycle events;
- feed the same client disconnect path used by ordinary transport failure;
- avoid calling reconnect methods directly.

When the gate opens it only permits future connection attempts. The production
reconnect coordinator must recover through its normal retry loop.

The test holds the gate closed long enough to guarantee multiple control
progress publications, initially three seconds. It should wait on observable
conditions rather than sleep for success; the fixed offline interval exists
only to create deterministic server activity.

The test hook is compiled only for Unity editor or test builds and does not
become a shipped player control surface. It may orchestrate transport disposal
and connection-attempt rejection, but it must not preserve callbacks, bind game
sessions, acknowledge messages, or invoke recovery completion.

Network proxies, firewall manipulation, and container termination are excluded
from the first implementation. They remain possible later resilience tests but
are not required to prove the framework contract defined here.

## Acceptance Scenario

Extend the existing three-node Unity PlayMode acceptance after the current
login, matchmaking, KCP attach, world-state, and movement checks:

1. Record control and realtime `GameSessionKey` values, connection serials,
   player id, room id, match id, world tick, and the latest control progress
   revision.
2. Close the Unity network gate for three seconds.
3. Observe both old connections end and both client channels enter recovery.
4. Confirm that the server continues publishing control progress while no
   callback is bound.
5. Open the gate.
6. Wait for control game-session resume and reliable replay.
7. Wait for realtime game-session resume and a fresh world state.
8. Submit movement and observe the authoritative player position change.

Required assertions:

- both new RPC connection serials differ from their old values;
- control `GameSessionKey` is unchanged;
- realtime `GameSessionKey` is unchanged;
- player, room, and match identities are unchanged;
- progress revisions produced during the offline window arrive in order;
- replayed progress is applied once despite possible duplicate delivery;
- the KCP endpoint reports reliable push disabled;
- the resumed world tick advances beyond the pre-fault tick;
- no historical KCP replay queue is created;
- input is rejected or suppressed during recovery and works after fresh state;
- no second login, rematch, or replacement game session occurs.

Failures must report the recovery phase, old and new session identities,
connection serials, reliable progress revisions, world ticks, and the current UI
state in the Unity test snapshot and existing test artifacts.

## Focused Test Coverage

### Framework tests

`tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj` must cover:

- endpoint override inheritance from the global default;
- endpoint-specific handshake advertisement;
- first binding captures game-session delivery policy;
- disconnect retains policy and expiration/termination removes it;
- resume with matching policy succeeds;
- resume with mismatched policy fails without rebinding;
- reliable session publication sequences and retains records;
- best-effort session publication creates no outbox record or metadata;
- replay and ack are disabled for best-effort sessions;
- remote publication is still sequenced only by the route owner;
- stale or missing routes do not create caller-side outbox records.

`tests/Lakona.Game.Client.Tests/Lakona.Game.Client.Tests.csproj` is required only
if framework client state changes. Existing handshake, inbox deduplication, and
cursor behavior must remain covered.

### Agar business tests

`samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
must cover:

- WebSocket endpoint reliable push enabled and KCP endpoint disabled;
- the new callback id and MemoryPack DTO shape;
- once-per-second control progress publication without online-state branches;
- KCP disconnect retains realtime game-session identity;
- expiration and match termination clear retained realtime identity;
- realtime reattach resumes the same `GameSessionKey`;
- mismatched player, token, room, match, or generation cannot resume;
- test-only network controls are excluded from normal player compilation.

### End-to-end acceptance

Run:

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

The existing audited Unity result validation remains mandatory. The acceptance
must fail if the reconnect assertions are skipped, ignored, inconclusive, or do
not appear in the expected test result.

## Documentation And Validation Plan

Before implementation is considered complete:

```powershell
dotnet build Lakona.slnx
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Client.Tests/Lakona.Game.Client.Tests.csproj --no-build
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-build
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
git diff --check
```

If solution-scale build or tests require restore, follow `CONTRIBUTING.md` and
request network permission on the first restoring command. Any skipped command
must be recorded with its exact reason and residual risk.

## Milestones And Review Gates

1. **Framework contract and focused tests**
   - add endpoint policy shape and policy ownership;
   - architecture review before runtime implementation.
2. **Owner-side reliable-push behavior**
   - implement handshake, publication, replay, ack, and lifecycle semantics;
   - focused review of session-policy and route-owner invariants.
3. **Agar server migration**
   - endpoint policy, progress callback, stable identities, realtime resume;
   - focused business tests and hotfix boundary review.
4. **Unity recovery and fault injection**
   - one recovery coordinator, pending UI state, test-only network gate;
   - PlayMode review for callback threading and cancellation.
5. **Three-node acceptance and documentation**
   - extend audited smoke, update authority docs and sample README;
   - final integration review, version graph guard, and repository hygiene.

No implementation milestone begins until this design is reviewed and approved.

## Explicit Non-Goals

- transparent migration of a game session to an endpoint with a different
  reliable-push policy;
- replay coalescing or payload-aware dropping by the framework;
- durable or replicated outbox storage across gateway process failure;
- gateway or battle node restart acceptance;
- network packet-loss, latency, jitter, or repeated flap simulation;
- cross-gateway game-session migration;
- infinite client retry;
- business-authored offline caches or resend loops;
- replay of historical KCP world-state callbacks when the KCP endpoint has
  reliable push disabled.

## Completion Criteria

The change is complete only when a single audited three-node Unity acceptance
demonstrates that a simultaneous WebSocket and KCP network loss destroys both
old RPC sessions, preserves and resumes both game sessions, replays reliable
control progress, resumes best-effort realtime state without historical replay,
and allows the same player to continue the same match without business
connection-state handling.
