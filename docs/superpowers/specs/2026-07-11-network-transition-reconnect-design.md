# Network-Transition Reconnect And Endpoint Reliable Push Design

## Status

Revised through a `grill-with-docs` review and awaiting final approval. This
document defines the intended contract before any runtime or sample
implementation begins.

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

This design revision is verified against repository `main` at `9da1a478`.

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
- Game Session retention, reliable-push retention, client-session route lease,
  and Agar retry attempts use independent time limits;
- client-session routes use the general cluster route lease and are not renewed
  by the game heartbeat, so the route may expire while its Game Session is still
  active or resumable;
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

### Player Session

The Agar-owned continuity of an authenticated player, including current room
and match participation. It references a Control Game Session and, while the
player is attached to realtime gameplay, a Realtime Game Session.

The Control Game Session is the recovery anchor. Loss of only the Realtime Game
Session does not destroy an otherwise valid Player Session.

If the Control Game Session is `StateLost`, the framework can no longer prove
reliable-push outbox, sequence, and client-cursor continuity. That outcome does
not qualify as seamless recovery. The client exits the reconnect flow and uses
an explicit reauthentication or state-rebuild path; this design does not promise
to retain the current Player Session in that case.

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
- `Lakona.Game.Abstractions`
  - handshake contract for the negotiated Game Session Resume Window.
- `Lakona.Game.Client`
  - handshake application, contiguous reliable-sequence validation, cursor, and
    gap outcomes.
- `Lakona.Rpc.Analyzers`
  - generated client surface for the negotiated Resume Window if the generated
    `LakonaGameClient` API exposes it alongside existing handshake policy.
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
  - generated default business endpoints explicitly emit
    `"ReliablePush": true`;
  - template and source-shape guards reject reliance on the removed global
    switch or an implicit enabled default.

### Coupling assessment

Endpoint policy resolution, game-session policy retention, owner-side reliable
push, handshake, and session resume are strongly coupled and require one
continuity-preserving implementation owner. Agar's realtime resume must be
implemented against that final framework contract by the same owner or as the
immediately following milestone.

Documentation wording checks, stale-name scans, and final checklist verification
are independent only after runtime behavior is stable.

### Compatibility stance

This design deliberately makes a breaking configuration cleanup:

- remove the global `Lakona:ReliablePush:Enabled` setting;
- reliable push defaults to disabled on every endpoint;
- only an explicit endpoint `"ReliablePush": true` enables outbox, sequence,
  acknowledgement, and replay;
- no wire-protocol shape change is needed for the enabled flag because
  `GameServerHello` already carries the endpoint's effective policy;
- existing business notification calls remain unchanged.

Internal session and delivery-policy APIs may be redesigned cleanly. The old
global switch and inheritance behavior are removed rather than preserved
through a compatibility shim, consistent with repository policy.

### Versioning impact

Expected shippable changes require version bumps for
`Lakona.Game.Abstractions`, `Lakona.Game.Client`, `Lakona.Game.Server`,
`Lakona.Rpc.Analyzers`, and `Lakona.Tool`. The final implementation must run the
package-version graph guard and bump every additional package in the dependency
closure that ships changed content.

## Endpoint Reliable-Push Configuration

### One resume window

The server exposes one public recovery-time setting:

```json
{
  "Lakona": {
    "Sessions": {
      "ResumeWindowSeconds": 120
    }
  }
}
```

The default is 120 seconds. The Game Session captures an exact
`ResumeDeadlineUtc` when its RPC connection disconnects. `TryResume` compares
against that deadline directly; cleanup timing must not extend the public
contract.

The same window governs:

- Game Session resume eligibility;
- automatic client reconnect deadline;
- reliable-push pending lifetime for the disconnected Game Session;
- how long a disconnected client-session route must remain usable.

The server sends the effective window in `GameServerHello.SessionResume.Window`.
The client uses the negotiated deadline instead of a configured retry count or
Agar-specific maximum-attempt constant. Cancellation, logout, termination,
unauthorized token results, and explicit state loss may end recovery earlier.

`ReliablePush.Retention` and
`Lakona:Sessions:Cleanup:DisconnectedRetentionSeconds` cease to be independent
public time settings. Cleanup interval remains an operational garbage-collection
setting and does not affect resume eligibility.

Client-session routes are renewed by the game heartbeat while connected. Each
renewal extends the route far enough to cover the complete Resume Window after
the last successful heartbeat. Expiration or termination removes the route.
The general cluster route lease remains cluster infrastructure and no longer
defines the client-session recovery window.

### Endpoint delivery policy

Each client-facing endpoint gains an optional explicit opt-in property:

```json
{
  "Lakona": {
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
endpoint.ReliablePush == true
```

Omitting the endpoint property disables reliable push. There is no global
enable switch and no inheritance rule.

The framework default and generated-project default are intentionally different:

- a hand-authored endpoint is best effort unless it explicitly opts in;
- `lakona-tool new` explicitly emits `"ReliablePush": true` for its default
  business endpoint so the generated application demonstrates reliable push;
- generated KCP projects also opt in because transport choice does not determine
  callback semantics;
- Agar explicitly enables the WebSocket control endpoint and explicitly disables
  the KCP world-state endpoint.

The property configures framework notification delivery, not transport
reliability. KCP may still retransmit packets within one live KCP connection.
Setting endpoint reliable push to `false` means callback commands are not placed
in the game-session outbox for replay across a later RPC session.

### Pending capacity and overflow

The only public reliable-push resource limit is:

```json
{
  "Lakona": {
    "ReliablePush": {
      "MaxPendingPerSession": 256
    }
  }
}
```

Capacity is a resource guard, not a second recovery-time setting. The outbox
must not silently evict old records and continue advertising an intact reliable
sequence. When a reliable Game Session reaches its pending capacity, the owner
atomically marks reliable continuity as `StateRefreshRequired`, emits low-
cardinality diagnostics, and stops ordinary replay for that session generation.

The Game Session may still exist, but a later resume cannot be reported as an
ordinary seamless resume. Agar treats `StateRefreshRequired` on its Control Game
Session as recovery failure because full business-state refresh is outside this
design. Best-effort endpoints do not create pending records and are unaffected
by this limit.

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

Sessions created through advanced unbound APIs capture disabled reliable-push
policy at creation. Their later callback binding must match that fixed policy.
Reliable sessions therefore require connection-bound creation from an endpoint
that explicitly enables reliable push. The implementation should keep this
policy internal rather than exposing transport or endpoint names through
business session state.

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

The built-in Game Session registry and outbox are process-local. A client-only
network interruption leaves the gateway owner alive and is the normal seamless-
recovery case. If that owner process fails or restarts, the built-in stores lose
session, pending, and sequence continuity; recovery returns `StateLost` and must
not claim a complete replay. Durable or replicated stores may be supplied by a
future or application-specific implementation without changing business
`NotifyAsync` calls, but are outside this design and its three-node acceptance.

Seamless control recovery also requires Gateway Affinity: during the Resume
Window, the new WebSocket RPC session must return to the same gateway owner.
Multi-gateway deployments provide this through load-balancer affinity,
consistent routing, or an owner-directed public endpoint. A different gateway
does not create a replacement Control Game Session and does not redirect the
client in this design; it returns `StateLost`. Distributed session/outbox state,
cross-gateway migration, and resume redirect protocols are explicitly excluded.

### Ordered replay barrier

Reliable push guarantees a contiguous application order for one Game Session
generation:

- transport delivery is at least once;
- sequence assignment is monotonic at the route owner;
- application is strictly contiguous and at most once at the client;
- cumulative acknowledgement covers only the contiguous applied prefix.

When a callback is rebound, the owner enters a per-session `Replaying` state.
Pending records are delivered in sequence order. New publications continue to
append to the same outbox but cannot bypass the replay barrier and reach the
callback ahead of older pending records. After delivery catches up to the
current tail, the owner transitions to `Live` delivery. Publication, replay,
and acknowledgement state transitions are serialized per Game Session rather
than protected only by a process-wide snapshot lock.

The client applies these rules:

```text
sequence == lastApplied + 1  -> apply, persist cursor, acknowledge
sequence <= lastApplied      -> duplicate; do not apply, acknowledge prefix
sequence > lastApplied + 1   -> gap; do not apply or advance acknowledgement
```

A detected gap transitions recovery to `StateRefreshRequired`. The client must
not apply the later command and then classify the missing prefix as duplicate.

### Handshake, acknowledgement, and replay

- `GameServerHello.ReliablePush` reports the endpoint's effective policy.
- A reliable endpoint advertises `Enabled=true` and `AckRequired=true`.
- A best-effort endpoint advertises both values as false.
- Replay runs only for game sessions whose retained policy is reliable.
- Best-effort sessions never create reliable metadata or pending records.
- Pending overflow transitions the session generation to
  `StateRefreshRequired`; it never drops a prefix and continues normal replay.
- Rebound delivery uses a per-session replay barrier, and live publication
  cannot overtake pending replay.
- Client acknowledgement advances only over a contiguous applied prefix; a gap
  is `StateRefreshRequired`.
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
- a per-match monotonic `ProgressRevision`;
- `PublishedAtUtc`, assigned by the server when the update is published.

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
session only when no realtime game-session identity exists.

If the Control Game Session resumes successfully but the retained Realtime Game
Session returns `StateLost`, the Player Session remains valid. The control plane
revalidates the authoritative player, room, and match state, clears the stale
realtime identity, creates a replacement Realtime Game Session, and attaches it
to the same match. This is a degradation path, not the expected short-network-
transition path. The primary acceptance still requires both original game
sessions to resume unchanged.

Realtime identity is cleared when:

- the realtime game session expires after the Game Session Resume Window;
- it is explicitly terminated;
- the room or match ends;
- product policy deliberately supersedes it.

It is not cleared merely because its RPC session disconnected.

## Unity Recovery State Machine

### Responsibility boundary

This design does not add a generic framework recovery coordinator. Authentication,
control login, room membership, realtime attach, and the dependency between the
two channels are application policy that Lakona cannot infer.

The framework owns:

- the handshake-provided Game Session Resume Window;
- Game Session resume and callback rebinding;
- endpoint reliable-push policy;
- outbox, ordered sequence, acknowledgement, replay barrier, deduplication, and
  gap detection;
- explicit `StateRefreshRequired`, `StateLost`, unauthorized, and terminated
  outcomes.

Agar owns one application-level recovery state machine that creates new
WebSocket and KCP RPC sessions, performs reconnect login and realtime attach,
and maps final outcomes to its UI. It uses the negotiated Resume Window and does
not define a competing retry-count limit.

Business notification publishers remain connection-agnostic: they publish
through `IClientNotifications` and do not implement online checks, caching,
retry, sequence, acknowledgement, or replay.

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
9. Control Game Session `StateLost` or termination exits recovery through the
   existing explicit reset path.
10. Realtime Game Session `StateLost` after successful control resume may create
    a replacement Realtime Game Session only after the control plane confirms
    the same Player Session, room, and match remain authoritative.

Retry timing remains bounded and cancellation-safe. The handshake-provided Game
Session Resume Window is the retry deadline; the client uses backoff between
attempts but does not stop early because of a fixed attempt count.

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
2. Record `offlineStart` and close the Unity network gate for three seconds.
3. Observe both old connections end and both client channels enter recovery.
4. Confirm that the server continues publishing control progress while no
   callback is bound.
5. Record `offlineEnd` and open the gate.
6. Wait for control game-session resume and reliable replay.
7. Wait for realtime game-session resume and a fresh world state.
8. Submit movement and observe the authoritative player position change.

Required assertions:

- both new RPC connection serials differ from their old values;
- control `GameSessionKey` is unchanged;
- realtime `GameSessionKey` is unchanged;
- player, room, and match identities are unchanged;
- at least two replayed progress updates have server `PublishedAtUtc` values
  within the offline window, allowing a small host/container clock tolerance;
- progress revisions produced during the offline window arrive in order;
- replayed progress is applied once despite possible duplicate delivery;
- replay catches up and subsequent live progress remains contiguous;
- the KCP endpoint reports reliable push disabled;
- the resumed world tick advances beyond the pre-fault tick;
- no historical KCP replay queue is created;
- input is rejected or suppressed during recovery and works after fresh state;
- no second login, rematch, or replacement game session occurs.

Failures must report the recovery phase, offline window, old and new session
identities, connection serials, reliable progress revisions and publish times,
last reliable sequence, world ticks, and the current UI state in the Unity test
snapshot and existing test artifacts.

## Focused Test Coverage

### Framework tests

`tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj` must cover:

- omitted endpoint policy defaults to disabled and explicit `true` enables it;
- the removed global enable setting is rejected by configuration/source guards;
- generated default endpoints explicitly opt in instead of relying on framework
  defaults;
- endpoint-specific handshake advertisement;
- handshake advertisement and client application of the Game Session Resume
  Window;
- exact deadline rejection independent of cleanup scan timing;
- heartbeat renewal keeps client-session routes valid through the Resume Window;
- first binding captures game-session delivery policy;
- disconnect retains policy and expiration/termination removes it;
- resume with matching policy succeeds;
- resume with mismatched policy fails without rebinding;
- reliable session publication sequences and retains records;
- best-effort session publication creates no outbox record or metadata;
- pending-capacity overflow marks continuity `StateRefreshRequired` without
  silent eviction or partial replay;
- concurrent replay and publication preserve contiguous application order;
- client gap detection refuses to apply or acknowledge a later sequence;
- replay and ack are disabled for best-effort sessions;
- remote publication is still sequenced only by the route owner;
- stale or missing routes do not create caller-side outbox records.

`tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj` must
cover handshake codec round trips for the Resume Window.

`tests/Lakona.Game.Client.Tests/Lakona.Game.Client.Tests.csproj` must cover
handshake application, negotiated window validation, contiguous sequence
application, duplicate acknowledgement, gap rejection, and cursor persistence.

`tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj` must cover
the generated client policy surface if the source generator exposes the Resume
Window.

`tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj` must cover explicit
`ReliablePush: true` generation and the removal of the global enable setting.

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
dotnet test tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Client.Tests/Lakona.Game.Client.Tests.csproj --no-build
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj --no-build
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-build
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
   - one Agar-owned recovery state machine, pending UI state, test-only network
     gate;
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
- seamless recovery after the Game Session route-owner process restarts;
- gateway or battle node restart acceptance;
- network packet-loss, latency, jitter, or repeated flap simulation;
- cross-gateway game-session migration;
- cross-gateway resume redirect or shared Game Session/outbox state;
- seamless control recovery without Gateway Affinity;
- infinite client retry;
- a public generic client recovery coordinator or framework-owned business
  authentication/room-attach workflow;
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
