# Session Lifecycle

Lakona owns one framework game session per accepted game RPC session. It does
not own account, player, character, room, or device aggregation.

This document defines session identity, callback binding, disconnect,
expiration, termination, resume, and the Gate / Watchdog / Agent composition
pattern.

For hotfix-backed service binding, see
[hotfix/service-binding.md](hotfix/service-binding.md).

## Core Decisions

- A `GameSessionKey` represents exactly one game RPC session.
- One bound RPC connection is associated with at most one active
  `GameSessionKey`.
- Multiple sessions for the same account, player, character, or room are
  user-managed business state.
- `EndpointName` and `GameEndpointName` are not user-facing concepts in
  generated binding, `ILakonaGameServer`, reliable push protocol state, hotfix call
  contexts, or session directory storage.
- `Lakona:Endpoints[]` remains transport listener configuration. It does not
  define framework session sub-identities.
- `GameSessionKey` is server/framework identity. Shared RPC DTOs, generated
  client code, and MemoryPack formatters must not expose, serialize, store, or
  echo it.

## Vocabulary

| Term | Meaning |
| --- | --- |
| RPC connection | One accepted transport connection known to the RPC server. |
| Connection id | Stable framework id for one accepted RPC connection while it exists. |
| Game session | One framework-owned game session identified by `GameSessionKey`. |
| Session callback binding | A callback contract instance bound to a game session and connection id. |
| Business session group | User-owned grouping such as account, player, character, room member, or device. |
| Transport endpoint | Listener configuration from `Lakona:Endpoints[]`; not part of session identity. |
| Business presence | User-owned online/offline policy derived from session lifecycle hooks. |

## Session Semantics

`GameSessionKey` identifies one framework-owned game RPC session. It is not an
account, player, character, room member, device, or transport channel group.

Starting a new session for an owner must not automatically invalidate other
sessions for the same owner. `OwnerKey` is a user-provided ownership label for
diagnostics, authorization, lookup, or user-maintained indexing. It is not a
framework uniqueness constraint.

If a game wants only one active session per account, it implements that policy
explicitly in server-side user code. If a game wants both a WebSocket control
session and a KCP realtime session for one character, user code stores that
grouping:

```txt
CharacterId
  -> ControlSession: GameSessionKey
  -> RealtimeSession: GameSessionKey
```

Terminating one `GameSessionKey` does not implicitly terminate another. User
code applies cross-session policy when that is the desired product behavior.

## Session Directory

The framework session directory stores sessions by `GameSessionKey`, not by
owner or endpoint. It is framework infrastructure, not a user-authored
`Server.App` business class such as the removed Agar `SessionDirectory`.

Each session stores callback bindings by callback contract type:

```txt
GameSessionKey
  -> ILoginCallback: connection id + callback + state
  -> IChatCallback: connection id + callback + state
```

Binding a callback contract for a session replaces only that callback contract.
Binding a different active `GameSessionKey` to a connection id that already has
an active session binding is invalid. User code must explicitly terminate or
expire the old session before reusing that RPC connection for another game
session.

The session directory or a companion tracker must support connection-id lookup
so the RPC lifecycle bridge can mark sessions disconnected when an RPC
connection closes.

Do not keep `SessionEndpointKey` in the target model. A game session no longer
has framework-owned endpoint children.

## Game Server API

User-facing game server APIs must stay session-oriented:

```csharp
public interface ILakonaGameServer
{
    ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindCurrentSessionAsync<TCallback>(
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

Exact method names can evolve, but the public model must not require users or
generated hotfix proxies to pass endpoint names.

## RPC Lifecycle Bridge

`Lakona.Rpc.Server` exposes neutral lifecycle hooks without referencing
`Lakona.Game`. `Lakona.Game.Server` registers an observer that turns RPC
lifetime into game session lifetime:

```txt
RPC session started
  -> game connection opened
  -> optional user lifecycle hooks

RPC session disconnected
  -> mark the game session bound to that connection disconnected
  -> publish one session-disconnected lifecycle hook
  -> cleanup later expires stale disconnected sessions
```

Session disconnection, expiration, and termination are separate events:

- Disconnection means the current RPC connection was lost and the session may
  still resume before retention expires.
- Expiration means disconnected session state was removed by cleanup policy.
- Termination means an explicit framework operation invalidated the session and
  optionally published a terminal notice.

User lifecycle hooks receive game-level context, not `RpcSession` and not
endpoint names. Business presence policy belongs behind these hooks. Stable
framework code owns the lifecycle bridge and required-contract validation.
Generated and sample App code uses strict zero-template hosting; the framework
registers the lifecycle bridge as part of the default game-server graph.

Replaceable presence, cleanup, room leave, and matchmaking decisions belong in
`Server.Hotfix` lifecycle classes such as `ChatSessionLifecycle`, not in App
lifecycle handlers, App runtime contract files, or `*LifecycleService` classes.

## Hotfix Lifecycle Contract

The framework requires the hotfix assembly to implement the framework-owned
`IGameSessionLifecycle` contract when session lifecycle hooks are active:

```csharp
public interface IGameSessionLifecycle
{
    ValueTask SessionDisconnectedAsync(GameSessionDisconnectedRequest request);

    ValueTask SessionExpiredAsync(GameSessionExpiredRequest request);
}
```

`SessionDisconnectedAsync` is published after an RPC connection bound to a game
session is marked disconnected. `SessionExpiredAsync` is published after cleanup
removes a stale disconnected game session. Both methods are invoked through
stable `[RpcMethod]` ids and hotfix lifecycle dispatch helpers; user-authored hotfix
implementations accept `HotfixLifecycleCall<TRequest>`.

Both request types carry framework session state only:

- `OwnerKey`: the game session owner key, such as a player id.
- `SessionId`: the framework session id.
- `Generation`: the owner session generation.
- `ConnectionId`: the last RPC connection associated with the event.
- `CallbackContractTypeNames`: callback contracts bound before disconnect or
  expiration.

Product policy still belongs in hotfix code, where it can map the session event
to presence, room, matchmaking, or other business cleanup.

## Handshake Gate

No business RPC dispatch occurs before `ClientHello` / `ServerHello`
completes. Handshake failure rejects the connection before user hotfix service
code runs. `ServerHello` reports the selected protocol version plus
server-owned reliable-push and heartbeat policies. The reliable-push handshake
policy is limited to whether reliable push is enabled and whether client acks
are required.

The game handshake is separate from transport connection setup:

```txt
transport accepted
  -> framework connection created
  -> ClientHello
  -> ServerHello
  -> business RPC enabled
```

Handshake payloads are framework-internal Lakona.Game messages. They are
encoded with `LakonaInternalCodec`, not with the endpoint-selected business
serializer. Framework-internal payloads stay on `LakonaInternalCodec`; the
endpoint business serializer is not part of the default framework handshake and
begins only at business RPC payloads after handshake succeeds.
`LakonaInternalCodec` also does not follow `Lakona:Cluster:Serializer`; the
cluster serializer is for node-to-node cluster,
notification-relay, and remote actor payloads.

`LakonaInternalCodec` is the framework-owned v1 payload codec for
Lakona.Game control messages. It covers `GameClientHello`, `GameServerHello`,
`GameHeartbeatRequest`, `GameHeartbeatReply`, `ReliablePushAckRequest`,
`ReliablePushAckOutcome`, and `SessionTerminationNotice`. New Lakona.Game
framework-internal payloads must add an explicit codec kind and layout instead
of routing DTOs through endpoint `IRpcSerializer`.

Lakona.Game control payload layouts are a package-set contract. This early
framework does not support mixing old and new Lakona.Game protocol packages in
one deployment; update the generated client, client runtime, abstractions, and
server runtime together.

`Lakona.Game.Abstractions` must remain free of concrete serializer package
dependencies such as `Lakona.Rpc.Serializer.Json`,
`Lakona.Rpc.Serializer.MemoryPack`, `MemoryPack`, or
`MemoryPack.Generator`. Framework-control DTOs must not require user projects
to add JSON converters or MemoryPack attributes. Malformed framework-internal
request payloads are protocol-level bad requests (`RpcStatus.BadRequest`), not
business failures.

Service id `0` is reserved for Lakona.Game framework-internal calls and
notifications. Generated business RPC contracts must use positive service ids
outside that reserved range.

Business RPC before a completed handshake is rejected with a structured
`HandshakeRequired` failure. `ClientHello` carries only `ProtocolVersion = 1`.
`ServerHello` returns the selected protocol version plus server-owned
reliable-push and heartbeat policies. Endpoint transport, endpoint serializer,
server node identity, server time, runtime, platform, game-version, build, and
capability metadata are application concerns and are not part of the default
framework handshake unless they gain concrete framework behavior.

## Framework Heartbeat

Game heartbeat is a framework RPC, not a business service method. Generated
`LakonaGameClient` starts one heartbeat loop after the handshake succeeds.

The heartbeat request does not carry the full `GameSessionKey` owner identity.
After the generated client starts a framework session, heartbeat carries the
client's `SessionId` and `SessionGeneration`. The server treats heartbeats
without session identity as connection-only heartbeats and only replays pending
reliable push records after the client reports the matching active session. This
upgrade is automatic; business code should not start a second session heartbeat
loop.

The default heartbeat policy uses a 15 second interval and 45 second timeout
unless the resolved server policy says otherwise.

Heartbeat request and reply payloads are encoded with `LakonaInternalCodec`.
They do not require JSON converters, MemoryPack formatters, or generated
business contract DTO metadata.

Heartbeat replies report framework session status:

- `Ok`: the connection or bound session is still valid.
- `StateLost`: the bound session can no longer be resumed.
- `Terminated`: the bound session reached a terminal server-side outcome.

Network errors, heartbeat RPC failures, or heartbeat timeouts move the generated
client to a reconnecting or failed state. Lakona v1 does not provide automatic
reconnect; users dispose the generated client and create a new one.

## Business Session State

The framework owns `GameSessionKey`, callback bindings, resume tokens, reliable
push protocol state, route indexes, and transport connection state. Business
code owns account, player, character, room, and device policy.

### Session Items

Session items are server-side session metadata for latency-sensitive cached
metadata. They are not durable business state.

Only scalar values supported by `GameSessionItemValue` are valid: `string`,
`Int64`, and `Boolean`. Valid examples include `roomId`, `matchId`,
`sessionKind`, realtime session identity, and membership generation after
authoritative validation.

Session items must not store callbacks, transport objects, DI services, actor
instances or references, hotfix-defined class instances, mutable collections,
durable player data, or room membership authority.

Each session item container is created empty with its `GameSessionKey`. Items
are preserved across disconnect and resume for the same generation. Items are
cleared or inaccessible after termination, including terminal-state retention,
and removed when a disconnected session expires.

Session item keys are ordinal, case-sensitive, non-empty strings with bounded
length. Missing reads return `null` so latency-sensitive services can treat
absence as stale, unauthenticated, or not-yet-attached state. Setting or
removing an item for a missing or terminated session is a server programming
error, not a client business rejection.

Session items are never serialized to clients or shared RPC DTOs. They are not
framework uniqueness constraints and must not be emitted as metric tags.

Hotfix calls receive `CurrentSessionItems` as an immutable per-dispatch
snapshot captured before the hotfix method runs. Mutations through
`ILakonaGameServer` do not update that snapshot. Use `GetSessionItemAsync` for
fresh reads later in the same call.

Cached items must not bypass route freshness. A cached room id may choose an
actor key, but generated selectors still resolve placement unless a future node
lease or epoch design exists.

Games that need one player-level session aggregate should keep it in a business
actor such as `UserActor`, not in `Server.App` transport helpers. For example,
Agar's `UserActor` is the authority for player session policy and may store
business values such as player id, control session id, realtime session id,
connection generations, current room, match ticket, seat, and online state.

Business actors must not store callback objects, `RpcSession`, transport
objects, endpoint names, or framework callback binding containers. Framework
lifecycle requests carry stable data such as owner key, session id, generation,
connection id, and callback contract type names so hotfix lifecycle code can
update business state without holding transport objects.

Control and realtime channels are independent framework sessions. If losing one
channel should affect the other, the business actor applies that product policy.

## Gate / Watchdog / Agent

Gate / Watchdog / Agent is a recommended composition pattern, not a framework
class. It separates connection ownership, admission policy, and player-facing
state so each role can fail and scale independently.

```txt
Client -> Gate -> Watchdog -> Agent
```

| Role | Responsibility | Has business state | Failure impact |
| --- | --- | :---: | --- |
| Gate | Maintain client connections and forward messages. No business logic. | No | Client reconnects to another Gate; Agent can remain unchanged. |
| Watchdog | Authenticate, create or bind Agent, then exit the call chain. | Transient | Affects only new or resuming connections. |
| Agent | One-to-one player service. Holds session-facing state. | Yes | Affects only that player. |

The key point is that Gate is stateless. Public internet traffic can hit cheap
Gate nodes while player state lives behind Agents or actors.

For low-latency games, add a realtime channel:

```txt
Client -> Gate -> Watchdog -> Agent   control, low-frequency
Client -> KCP direct -> Room          realtime, high-frequency
```

The control and realtime channels are independent RPC sessions. Losing one does
not directly change the other unless user business code links them and applies
that policy.

Lakona mechanisms for the pattern:

| Need | Mechanism |
| --- | --- |
| Gate TCP/WebSocket listener | endpoint configuration and RPC server hosting |
| Gate to Agent routing | cluster routing and route directory |
| Watchdog auth and session bind | user auth plus `ILakonaGameServer.StartSessionAsync` |
| Agent per-player state | actor runtime with per-player `ActorId` |
| Reconnect to another Gate | resume token and session resume service |
| Realtime channel | KCP endpoint plus separate `GameSessionKey` |
| Reliable delivery | reliable push outbox/inbox |
| Server-initiated disconnect | `ILakonaGameServer.TerminateSessionAsync` |

## Server-Initiated Termination

When the server must remove a player from an active session, treat it as a
terminal lifecycle transition, not as a raw transport close.

Recommended flow:

1. The Agent or server policy decides the session must end.
2. Server code calls `ILakonaGameServer.TerminateSessionAsync`.
3. Lakona marks the session terminal before notifying the client, so new
   business work for that session is rejected deterministically.
4. Lakona sends a fixed `SessionTerminationNotice` through the
   `ILakonaGameSessionCallback` bound to that `GameSessionKey`.
5. Lakona waits only up to `SessionTerminationOptions.NotifyTimeout`, then asks
   the configured session closer to close the stored connection id.
6. Later resume attempts return the terminal outcome when
   `KeepTerminalStateForResume` is enabled.

```csharp
await gameServer.TerminateSessionAsync(
    session,
    SessionTerminationReason.ReplacedByNewLogin,
    message: "This account logged in elsewhere.");
```

`SessionTerminationReason` is the only machine-readable reason.
`SessionTerminationNotice.Message` is optional display context and should not
become a second product-specific reason catalog.

The notice is best-effort. Correct clients must still handle the fallback path
where they only observe a disconnect and then receive
`SessionResumeStatus.Terminated` or another terminal outcome during
resume/login.

## Reliable Push And Resume

Reliable push uses the callback binding for a `GameSessionKey`. Resume policy
may rebind a disconnected game session to a new RPC connection when the resume
token and retention policy allow it.

Resume tokens are opaque client-facing credentials. They should not reveal
`GameSessionKey` or become business identity. User code may associate resume
state with account, player, character, room, or device records when it needs
product-specific policy.

Reliable push is framework-provided by default. Business code publishes through
the same notification API whether reliability is enabled or disabled. When
enabled, the framework owns sequence assignment, ack handling, replay, pending
limits, and route lookup. When disabled, the same publish operation degrades to
immediate best-effort notification with no ack and no replay.

The current owner of the `GameSessionKey` route is the only node that assigns
reliable-push sequences, retains pending records, accepts acknowledgements, and
replays records for that session generation. A remote business node relays an
unsequenced notification intent to the route owner; it does not create a local
outbox record or attach authoritative reliable-push metadata. The route owner
adds the notification to its outbox before dispatching it through the locally
bound callback.

If no current route exists, or the resolved route generation is stale,
publication returns `RouteNotFound` without creating an outbox record on the
calling node. The built-in in-memory outbox is not migrated when an owner
process fails or a session generation moves to another node. Pending
notifications may therefore be lost during owner failure; durable or replicated
outboxes remain an application-provided infrastructure choice.

The public configuration switch is:

```json
{
  "Lakona": {
    "ReliablePush": {
      "Enabled": true
    }
  }
}
```

The default is `true`. Generated development projects usually omit this key
because the default is derived by the framework. Setting it to `false` is an
explicit opt-out.

Business services must not expose reliable-push ack RPC methods, and
`ILakonaGameServer` must not expose reliable-push publish, replay, or ack
methods. Ack messages and replay bookkeeping are framework-owned protocol and
runtime behavior; replay support, pending limits, and delivery bookkeeping are
not handshake-negotiated client settings. The server reports only the
reliable-push enabled/ack-required policy in `ServerHello`; clients do not need
to know whether the server uses an in-memory store, durable store, plugin, or
built-in implementation.

Business notification APIs should express the intended session target and let
the framework resolve delivery:

```csharp
await clientNotifications
    .ForSession(sessionKey)
    .NotifyAsync<IPlayerCallback>(
        callback => callback.OnMatchmakingStatusAsync(update),
        cancellationToken);
```

The notification lambda is awaitable. Generated or runtime notification
dispatch must wait for the actual outbound send outcome even when the shared
callback contract method returns `void`; otherwise a reliable notification can
be reported as delivered while the transport send is still pending or has
failed.

The lambda-capture form is the business-facing intent API, but the current
runtime capture mechanism is not a permanent design commitment. Future
notification work should prefer generated typed command helpers over
`DispatchProxy`-style runtime capture so callback method selection is checked
at build time and the "exactly one callback method" rule is easier to reason
about.

User-targeted notification policy remains business-layer responsibility in this
iteration. The framework does not own session kind or expose user-and-kind
targeting. Games may still keep business presence and product session policy in
a user actor. Reliable push record identity is derived inside the framework from
the captured callback command; applications do not choose reliable versus
immediate delivery per notification.

`ClientNotificationStatus` reports the immediate dispatch attempt only:
`Delivered`, `RouteNotFound`, `CallbackUnavailable`, or `Failed`. It does not
expose outbox acceptance or replay bookkeeping. When the route owner accepted a
reliable notification but its local callback is temporarily unavailable, the
owner retains the record for framework replay. `RouteNotFound` means no owner
accepted the intent and no framework outbox contains it; business code may
apply product-specific retry policy for that result.

## Validation Requirements

Tests and source scans should reject these patterns in generated projects and
framework-facing docs:

```txt
EndpointName
GameEndpointName
SessionEndpointKey
OnEndpointBound
OnEndpointDisconnected
OnEndpointExpired
RpcSession.Disconnected +=
```

Allow those names only in tests that intentionally cover removed API behavior
or in explicitly historical release notes.
