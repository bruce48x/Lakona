# Gate / Watchdog / Agent — Connection Management Pattern

This is a recommended architecture pattern, not a framework class. It describes how to compose Lakona.Game's existing infrastructure into a proven connection management model. The pattern originates from [skynet](https://github.com/cloudwu/skynet).

## The pattern

```
Client ──TCP──→ Gate ──→ Watchdog ──→ Agent (one per player)
```

| Role | Responsibility | Has business state | Failure impact |
|------|---------------|:---:|------|
| **Gate** | Maintain TCP connections, forward messages. No business logic. | No | Disconnect → reconnect to another Gate, agent unchanged |
| **Watchdog** | Authenticate, create/bind Agent, then exit the call chain. | Transient | Only affects new connections |
| **Agent** | One-to-one player service. Holds all session state. | Yes | Only affects that player |

The key insight: **Gate is stateless.** If a Gate process goes down, the client reconnects to another Gate, Watchdog finds the existing Agent, and the session continues. Cheap Gate nodes can be exposed to the public internet; expensive Agent nodes stay behind.

## Dual-channel variant

For low-latency games (fighting games, FPS), add a realtime channel that bypasses Gate:

```
                    ┌─── Gate ─── Watchdog ─── Agent (control, low-freq)
Client ──┬──────────┤
         │
         └─── KCP direct ─── Room (realtime, 30fps)
```

The control channel handles login, matchmaking, reconnect. The realtime channel handles frame input and state snapshots. They are independent — losing one doesn't impact the other.

## How to implement with Lakona.Game

Lakona.Game provides all the mechanisms. The pattern is just composition:

| What you need | Lakona.Game mechanism |
|---------------|-------------------|
| Gate: TCP/WS listener | `IULinkRpcServerConfigurator` with TCP or WebSocket transport |
| Gate → Agent routing | `IClusterRouter` + `IRouteDirectory` |
| Watchdog: auth + agent creation | `IGameSessionTokenValidator` + `ILakonaGameServer.StartSessionAsync` |
| Agent: per-player service | `IActorRuntime` with per-player `ActorId` |
| Reconnect to another Gate | `GameSessionResumeService` with resume token |
| Realtime channel | `IULinkRpcServerConfigurator` with KCP transport, separate endpoint |
| Reliable delivery | `IReliablePushOutbox` + `IReliablePushInbox` |
| Server-initiated disconnect | `ILakonaGameServer.TerminateSessionAsync` + `ILakonaGameSessionCallback` |

## Server-initiated session termination

When the server must remove a player from an active session, treat it as a terminal session lifecycle transition, not as a raw transport close. The recommended flow is:

1. The Agent or server policy decides the current session must end.
2. Server code calls `ILakonaGameServer.TerminateSessionAsync`.
3. Lakona.Game marks the session terminal before notifying the client, so new business work for that session is rejected deterministically.
4. Lakona.Game sends a fixed `SessionTerminationNotice` through `ILakonaGameSessionCallback.OnSessionTerminatedAsync` on `GameEndpointName.Control` by default.
5. Lakona.Game waits only up to `SessionTerminationOptions.NotifyTimeout`, then asks the configured endpoint closer to close the stored connection id.
6. Later resume attempts return the terminal outcome when `KeepTerminalStateForResume` is enabled.

The common server call should stay short:

```csharp
await gameServer.TerminateSessionAsync(
    session,
    SessionTerminationReason.ReplacedByNewLogin,
    message: "This account logged in elsewhere.");
```

Games with multiple client-facing endpoints may use the overload that accepts `GameEndpointName`. Most games should keep termination notices on the control endpoint; realtime channels should be closed as a consequence of session termination rather than defining their own kick protocol.

The client callback endpoint implements the fixed framework callback:

```csharp
public sealed class ClientCallback : ILakonaGameSessionCallback
{
    public ValueTask OnSessionTerminatedAsync(
        SessionTerminationNotice notice,
        CancellationToken cancellationToken = default)
    {
        client.ApplySessionTerminationNotice(notice);
        return ValueTask.CompletedTask;
    }
}
```

`SessionTerminationReason` is the only machine-readable reason. `SessionTerminationNotice.Message` is optional display context and should not become a second product-specific reason catalog.

The notice is best-effort. No framework can guarantee that a final packet is delivered before the network disappears. Correct clients must still handle the fallback path where they only observe a disconnect and then receive `SessionResumeStatus.Terminated` or another terminal outcome during resume/login.

## When to use which variant

| Game type | Recommendation |
|-----------|---------------|
| Turn-based, casual, light MMO | Classic Gate → Watchdog → Agent. Single TCP/WS connection. |
| Real-time PvP, fighting, FPS | Dual-channel. Control via Gate, realtime via KCP direct to Room. |
| Single-server, single-player | Don't use this pattern. One process, no Gate needed. |
