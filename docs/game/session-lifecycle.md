# Session Lifecycle

Status: current architecture reference
Date: 2026-06-16
Audience: maintainers and contributors

Lakona.Game owns one framework game session per accepted game RPC session. It
does not own account, player, character, room, or device aggregation.

For generated service binding, see
[Generated Hotfix Service Binding](generated-hotfix-service-binding.md).
For server-initiated disconnect composition, see
[Gate / Watchdog / Agent](gate-watchdog-agent.md).

## Purpose

This document defines game session identity, callback binding, disconnect,
expiration, termination, and resume behavior. It is the session lifecycle
reference for generated projects, reliable push, hotfix call contexts, and
business lifecycle hooks.

## Core Decisions

- A `GameSessionKey` represents exactly one game RPC session.
- One bound RPC connection is associated with at most one active
  `GameSessionKey`.
- Multiple game RPC sessions for the same account, player, character, or room
  are user-managed business state.
- `EndpointName` and `GameEndpointName` are not user-facing concepts in
  generated service binding, `ILakonaGameServer`, reliable push APIs, hotfix
  call contexts, or session directory storage.
- `Lakona.Game:Endpoints[]` remains transport listener configuration. It does
  not define framework session sub-identities.
- `GameSessionKey` is server/framework identity. Generated shared RPC DTOs,
  generated client code, and MemoryPack formatters must not expose, serialize,
  store, or echo it.

## Vocabulary

| Term | Meaning |
| --- | --- |
| RPC connection | One accepted transport connection known to the RPC server. |
| Connection id | Stable framework id for one accepted RPC connection while it exists. |
| Game session | One framework-owned game session identified by `GameSessionKey`. |
| Session callback binding | A callback contract instance bound to a game session and connection id. |
| Business session group | User-owned grouping such as account, player, character, room member, or device. |
| Transport endpoint | Listener configuration from `Lakona.Game:Endpoints[]`. It is not part of session identity. |
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

The session directory stores sessions by `GameSessionKey`, not by owner or
endpoint.

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
endpoint names. Business presence policy belongs in these hooks.

## Server-Initiated Termination

When the server must remove a player from an active session, treat it as a
terminal session lifecycle transition, not as a raw transport close. The
recommended flow is:

1. The Agent or server policy decides the current session must end.
2. Server code calls `ILakonaGameServer.TerminateSessionAsync`.
3. Lakona.Game marks the session terminal before notifying the client, so new
   business work for that session is rejected deterministically.
4. Lakona.Game sends a fixed `SessionTerminationNotice` through the
   `ILakonaGameSessionCallback` bound to that `GameSessionKey`.
5. Lakona.Game waits only up to `SessionTerminationOptions.NotifyTimeout`, then
   asks the configured session closer to close the stored connection id.
6. Later resume attempts return the terminal outcome when
   `KeepTerminalStateForResume` is enabled.

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

## Validation Requirements

Tests and source scans should reject these patterns in generated projects and
current framework-facing docs:

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
