# Framework-Managed Game Session Recovery Design

## Status

Approved direction; implementation has not started. This document is the
architecture review gate for framework-managed reconnect. Code changes must not
begin until this contract has been reviewed as a whole.

This design supersedes sample-owned reconnect orchestration in
`Game.Unity.Agar`. It also defines the recovery model that
`Game.Godot.Chat` must validate instead of adding business-level resume fields.

## Scope Checkpoint

### Goal

After a business operation establishes a Game Session, Lakona automatically
replaces a failed transport/RPC Session, restores the retained Game Session,
rebinds framework callbacks, and resumes the endpoint's configured push
delivery semantics. Business contracts and business services do not carry or
branch on reconnect data.

### Affected Surfaces

- `Lakona.Game.Abstractions`: framework handshake and recovery outcomes.
- `Lakona.Game.Client`: connection-generation ownership, retry lifecycle,
  resume-ticket storage, reliable cursor storage, and stable public state.
- `Lakona.Game.Server`: ticket issuance and validation, Game Session rebinding,
  callback rebinding, lifecycle cleanup, route affinity, and replay barriers.
- `Lakona.Rpc.Analyzers`: generated stable client/API facade and framework
  control handlers.
- `Lakona.Tool`: generated configuration and package/version closure.
- `Game.Unity.Agar`: remove WS and KCP business reconnect orchestration.
- `Game.Godot.Chat`: add deterministic framework-recovery E2E coverage without
  adding resume fields to login/chat DTOs.
- Session, configuration, protocol, source-generation, and sample docs.

### Coupling Assessment

Handshake recovery, stable generated proxies, callback rebinding, client
connection state, and reliable replay ordering form one strongly coupled
lifecycle. They require one continuity-preserving implementation owner.

Once that contract compiles and has focused tests, the Agar migration, Godot
Chat migration, documentation scan, and package/version verification are
independent slices with disjoint primary write scopes.

### Compatibility Stance

Lakona is early stage. Prefer the smaller long-term API over preserving sample
reconnect DTOs or parallel manual/automatic recovery paths. Remove obsolete
sample fields and branches rather than deprecating them.

### Validation Plan

- `Lakona.Game.Abstractions.Tests`
- `Lakona.Game.Client.Tests`
- `Lakona.Game.Server.Tests`
- `Lakona.Rpc.Analyzers.Tests`
- `Lakona.Tool.Tests`
- Agar business logic tests
- Agar Docker three-node Unity PlayMode E2E
- `samples/Game.Godot.Chat/test-game-godot-chat-e2e.ps1`
- Godot Chat E2E script self-tests when the script changes
- repository guards, documentation consistency, package graph, solution build,
  and solution tests

### Versioning Impact

At minimum, implementation changes require version bumps for
`Lakona.Game.Abstractions`, `Lakona.Game.Client`, `Lakona.Game.Server`,
`Lakona.Rpc.Analyzers`, and `Lakona.Tool`. Additional package changes follow
the final dependency diff.

## Design Principles

1. A transport connection and RPC Session are disposable connection
   generations. They are never resumed or reused.
2. A Game Session is retained framework state and may be rebound to a new RPC
   Session within the negotiated resume window.
3. Once a Game Session exists, short network recovery is normal framework
   behavior, not a business feature or an everyday configuration choice.
4. Reliable push is a separate delivery guarantee. The framework cannot infer
   whether application push data represents durable events or replaceable
   snapshots.
5. The framework may only report successful recovery when Game Session state
   and the configured delivery guarantee are intact.
6. In-flight business RPC requests are not automatically replayed.
7. Process restart, cross-gateway redirect, and distributed recovery remain out
   of scope.

## Public Conceptual Model

New users learn one rule:

> After a Game Session is established, Lakona automatically reconnects and
> restores it during the server-provided recovery window.

They make one endpoint-level business decision:

> Must server push produced while this endpoint is offline be delivered after
> recovery?

The configuration remains:

```json
{
  "Lakona": {
    "Sessions": {
      "ResumeWindowSeconds": 60
    },
    "Endpoints": [
      {
        "Name": "control",
        "Transport": "websocket",
        "ReliablePush": true
      },
      {
        "Name": "realtime",
        "Transport": "kcp"
      }
    ]
  }
}
```

Both endpoints recover their Game Sessions automatically. The control endpoint
retains and replays unacknowledged push. The realtime endpoint resumes with new
push only and does not replay historical world-state frames.

There is no ordinary `AutoReconnect` endpoint setting. Automatic recovery is a
framework invariant after session establishment. If a future advanced client
opt-out is required, it must not appear in generated starter configuration and
must not create a second manual resume protocol.

## Public API Boundary

Business contracts must not expose framework recovery data. In particular,
application DTOs must not contain fields whose purpose is reconnect:

- `Reconnect`
- `SessionId`
- `SessionGeneration`
- `ResumeSessionId`
- `ResumeSessionGeneration`
- `ResumeToken`
- `ResumeTicket`

Business code performs initial authentication and creates a Game Session in
the normal way. Framework control messages then provide the generated client
with an opaque recovery capability.

Normal application code remains:

```csharp
await client.ConnectAsync(cancellationToken);
await login.LoginAsync(request);
await chat.BindAsync(request);
```

There is no reconnect login call and no application call to
`ResumeSessionAsync`. The same generated `LakonaGameClient`, `Api`, service
proxy, and callback receiver references remain usable across connection
generations.

Business code handles only terminal recovery outcomes when product behavior is
required:

- `StateLost`: the retained Game Session no longer exists or cannot be reached.
- `StateRefreshRequired`: the Game Session exists but reliable delivery
  continuity is no longer provable.
- `Terminated`: the server intentionally ended the Game Session.

## Recovery Capability

### Opaque Ticket

The framework issues an opaque `ResumeTicket` after a Game Session becomes
active. It is a framework-control value, not a business DTO field. The
generated client stores it internally with the endpoint identity and reliable
cursor store.

For the process-local v1 design, the ticket is a cryptographically random
capability mapped server-side to exactly one Game Session generation. It is:

- unguessable;
- scoped to the endpoint recovery policy;
- valid no longer than the Game Session's resume deadline;
- removed on expiration or termination;
- never written to normal logs, metrics, diagnostics, or exception messages;
- transmitted only over the endpoint's protected transport in production.

Possession authorizes rebinding to that retained Game Session. Initial business
authentication decides when the Game Session may be created; reconnect does
not call business login again.

Ticket rotation is not part of the first implementation because losing a
rotated response creates a two-ticket acknowledgement protocol. The v1 ticket
is stable for one Game Session generation and revoked with that generation.
Rotation may be added later without exposing the ticket to business code.

### Ticket Delivery

When `ILakonaGameServer.StartSessionAsync` activates and binds a Game Session,
the server sends a reserved Lakona.Game framework notification describing
session establishment and carrying the opaque ticket. The generated client
registers this handler before business RPC is enabled and persists the ticket
before exposing the established session state.

Ticket delivery uses `LakonaInternalCodec` and reserved service id `0`. It does
not use the endpoint business serializer and does not become part of generated
business contracts.

## Handshake Recovery

`GameClientHello` gains an optional opaque resume ticket. A connection without
a ticket follows the existing handshake and permits initial business login.

A connection with a ticket follows this framework path:

```text
transport connected
  -> new RPC Session created
  -> ClientHello(ticket)
  -> ticket validated
  -> retained Game Session located
  -> endpoint policy checked
  -> new callback proxies rebound
  -> new RPC Session bound to Game Session
  -> ServerHello(recovery outcome)
  -> business RPC enabled
  -> reliable replay barrier released by framework heartbeat
```

`GameServerHello` reports one of:

- `NotRequested`: no resume ticket was supplied.
- `Resumed`: the original Game Session is bound to this RPC Session.
- `StateLost`: the ticket is unknown, expired, revoked, belongs to unavailable
  process-local state, or cannot be used on this endpoint.
- `StateRefreshRequired`: binding succeeded but reliable continuity was already
  lost.
- `Terminated`: terminal session state was retained for delivery.

Business RPC remains blocked until handshake recovery finishes. Handshake
failure never invokes application login or service code.

The client does not submit a claimed reliable cursor in the ticket handshake.
The server retains acknowledgement state and may replay duplicates; the client
cursor safely deduplicates them. This avoids trusting a client-supplied high
water mark that could skip undelivered commands.

## Callback Rebinding

Automatic recovery cannot depend on invoking a business RPC merely to acquire
a callback proxy. The server stores the callback contract types associated
with the disconnected Game Session. Endpoint-generated infrastructure must be
able to resolve the corresponding callback proxies for the new RPC Session and
bind them during handshake recovery.

This is framework-generated binding metadata. Application services do not
enumerate contracts, recreate proxies, or call `BindSessionAsync` as part of
reconnect.

Callback receivers are stable objects owned by the generated client facade.
Replacing the RPC Session changes the dispatch target, not the receiver object
visible to application code.

## Stable Generated Client Facade

The current single-use generated client model must change internally without
making application-held service proxies stale.

`LakonaGameClient` owns:

- immutable endpoint/transport construction options;
- stable callback receiver registrations;
- the current connection generation;
- the current transport and `RpcClientRuntime`;
- the opaque ticket and reliable cursor store;
- one recovery state machine and cancellation lifetime.

Generated `Api` and service proxies dispatch through a stable indirection that
resolves the current ready connection generation for every new call. They must
not permanently capture the first `RpcClientRuntime`.

An RPC call already assigned to a failed generation completes with a connection
failure. The framework never moves or replays it on the next generation.

## Client Recovery State Machine

The externally observable states are:

```text
Disconnected
  -> Connecting
  -> ConnectedWithoutSession
  -> SessionActive
  -> Reconnecting
  -> SessionActive

Reconnecting
  -> StateLost
  -> StateRefreshRequired
  -> Terminated
  -> RecoveryWindowExpired
```

Only one connection attempt and one recovery loop may own the client at a time.
Disconnect signals from superseded connection generations are ignored.

After an active session disconnects, retry uses bounded exponential backoff
with jitter and never extends beyond the negotiated absolute resume deadline.
Cancellation or disposal stops the loop. A successful recovery replaces the
connection generation atomically and returns to `SessionActive`.

The exact default backoff schedule is an implementation detail and must be
covered with a deterministic `TimeProvider` or scheduler abstraction. Tests do
not use wall-clock sleeps to prove retry behavior.

Before a Game Session ticket exists, an interrupted business login or other
in-flight call fails normally. The framework does not infer whether that
operation is safe to repeat.

## Reliable Push Semantics

`ReliablePush` remains endpoint-scoped, defaults to `false`, and is fixed for
the Game Session generation.

For `ReliablePush = true`:

- disconnected push is retained in the session outbox;
- after rebind, new live push is retained but withheld;
- the framework heartbeat establishes a replay barrier;
- pending commands are delivered in contiguous sequence order;
- only after replay begins may newer publication pass through the serialized
  outbox;
- acknowledgement is cumulative and must not create reentrant RPC deadlock;
- duplicate delivery is not applied twice;
- a sequence gap permanently poisons that generation;
- overflow produces `StateRefreshRequired` and low-cardinality diagnostics.

For `ReliablePush = false`:

- the Game Session and RPC connection still recover automatically;
- push produced while disconnected is not retained;
- recovery continues with newly produced push only.

The framework therefore does not need or expose an `AutoReconnect` setting to
represent these two delivery modes.

## Multiple Endpoints

Each generated `LakonaGameClient` instance recovers independently. Lakona does
not create a distributed transaction across the Agar WS control connection and
KCP realtime connection.

During a Wi-Fi-to-cellular transition both may enter `Reconnecting` and recover
their own Game Sessions:

- WS control: resume and replay reliable control notifications.
- KCP realtime: resume and continue current world-state push without history.

The application may derive a combined UI state, but it does not provide resume
identities or orchestrate the framework recovery protocol.

Endpoint relocation, matchmaking reassignment, and redirect remain business or
future cluster-routing concerns. V1 retries the same advertised endpoint and
requires affinity to the process that owns the retained session and outbox.

## Failure Semantics

Lakona reports recovery success only when the negotiated contract is true.

| Condition | Framework outcome | Business implication |
| --- | --- | --- |
| New RPC Session and original Game Session rebound | `Resumed` | Continue normally |
| Ticket absent from owning process or expired | `StateLost` | Re-enter product flow |
| Reliable gap or outbox overflow | `StateRefreshRequired` | Fetch/rebuild full state |
| Session intentionally terminated | `Terminated` | Apply terminal product flow |
| Deadline reached without a usable connection | `RecoveryWindowExpired` | Treat as state lost |
| In-flight RPC interrupted | RPC connection failure | Retry only with business idempotency |

No outcome silently creates a replacement Game Session. A new Game Session is
created only through an explicit normal business flow after the failure is
surfaced.

## Server Lifecycle And Cleanup

- RPC disconnect marks the Game Session disconnected and establishes one exact
  absolute resume deadline.
- Disconnect does not invoke business leave/cleanup that would invalidate
  resumability.
- Expiration and termination revoke the ticket, remove the route and outbox,
  release callback bindings, and invoke hotfix lifecycle cleanup exactly once.
- A late resume attempt returns `StateLost` but does not bypass the expiration
  lifecycle by directly deleting registry state.
- Heartbeat and route leases use the same negotiated session recovery window;
  no shorter duplicate retention parameter limits effective recovery.

## Deterministic Test Seam

Samples do not manipulate the operating-system network stack. Framework/client
tests expose an internal test-only connection gate or injectable transport
factory that can:

- close the active connection generation;
- reject new connection attempts while closed;
- reopen without changing server process or endpoint;
- expose connection-generation serials and recovery state transitions.

Tests synchronize on explicit connection/session events with bounded timeouts.
Fixed sleeps are permitted only to create a deliberate offline publication
window, never to decide whether recovery succeeded.

## Sample Migration

### Game.Unity.Agar

Remove business reconnect protocol from login and realtime attach contracts and
services, including reconnect booleans, SessionId/generation responses, and
resume fields. The Unity client no longer recreates business login or battle
attach flows merely to resume.

The three-node test must still prove:

- WS and KCP receive new RPC connection generations;
- both original Game Sessions remain active;
- WS handshake advertises reliable push and KCP does not;
- offline control notifications replay exactly once in order;
- KCP resumes with fresh world state rather than historical frames;
- post-recovery input still changes authoritative state;
- no business reconnect field or branch exists in sample contracts/services.

### Game.Godot.Chat

Do not add reconnect fields to `LoginRequest` or `LoginReply`. Extend the
dedicated real WebSocket/MemoryPack E2E with a framework connection gate and two
clients:

1. Client A logs in and binds Chat normally.
2. Client B logs in.
3. A's connection generation is closed while its Game Session remains.
4. B sends multiple numbered messages while A is offline.
5. A's generated client automatically reconnects.
6. A receives the offline messages once, in order, without another login or
   chat bind RPC.
7. A sends a new message using the service proxy it held before disconnect.

Room membership must use stable Game Session identity rather than RPC
`ConnectionId` wherever membership must survive reconnect. Presence leave is
published only on Game Session expiration or termination.

## Milestones And Review Gates

1. **Protocol and API shape**: ticket control messages, handshake outcomes,
   stable public state, and old-surface scans. Architecture review required.
2. **Server recovery core**: ticket registry, handshake resume, callback
   rebinding, lifecycle cleanup, and focused exact-deadline tests. Lifecycle and
   security review required.
3. **Client connection generations**: stable facade/proxy indirection, retry
   state machine, ticket/cursor ownership, and deterministic scheduler tests.
   Concurrency review required.
4. **Reliable replay integration**: replay-pending barrier, nonblocking ACK,
   gap/overflow terminal behavior, and reconnect integration tests. Ordering
   review required.
5. **Generator and tool migration**: generated wrapper shape, default config,
   package closure, and source-generation snapshots. API review required.
6. **Agar migration and E2E**: remove business reconnect orchestration and pass
   the three-node Unity test.
7. **Godot Chat migration and E2E**: validate framework-owned reconnect through
   the dedicated sample script.
8. **Documentation, versions, hygiene, and final two-axis review**.

## Explicit Non-Goals

- Automatically retrying business RPC requests.
- Distributed Game Session or outbox persistence.
- Cross-gateway redirect or recovery after gateway restart.
- Replaying historical best-effort world-state push.
- Choosing product UI after terminal recovery failure.
- Inferring endpoint relocation or matchmaking reassignment.
- Hiding `StateLost` or `StateRefreshRequired` behind a new session.

## Acceptance Criteria

The design is complete only when all of the following are true:

1. A new user configures no reconnect switch and writes no reconnect business
   branch for normal short network transitions.
2. Business DTOs in Agar and Godot Chat contain no framework recovery identity.
3. Application-held generated client, API, service proxy, and callback receiver
   references remain valid across RPC connection generations.
4. In-flight RPC calls fail rather than being replayed.
5. Reliable and best-effort endpoints both recover Game Sessions, with only the
   reliable endpoint replaying offline push.
6. Exact resume-window expiration produces lifecycle cleanup and explicit
   failure.
7. Gateway/process loss produces `StateLost`; no redirect is attempted.
8. Agar three-node and Godot Chat real E2E tests prove recovery without calling
   business login/bind/attach again.
9. Public docs expose the simple conceptual model and move implementation
   details such as tickets and connection generations to advanced/framework
   sections.

