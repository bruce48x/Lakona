---
name: lakona-implement-session-lifecycle
description: Implement or update Lakona Game Session lifecycle policy in Server.Hotfix using IGameSessionLifecycle. Use when handling disconnect, resume-window retention, expiration, control versus realtime session cleanup, stale lifecycle events, presence or room cleanup, or fixing Hotfix lifecycle binding and tests.
---

# Implement a Lakona Session Lifecycle

Implement product cleanup policy around Lakona's resumable Game Session. Keep connection loss, recovery-window expiration, explicit termination, and product-level player sessions as distinct events.

## Workflow

1. Read the repository instructions and the project's session, Hotfix, actor, and testing documentation before editing.
2. Locate the existing `IGameSessionLifecycle` binding, session configuration, current control and realtime session ownership, business actors, room or matchmaking cleanup paths, and lifecycle tests.
3. Update the unique existing lifecycle implementation in place. Create one only when no binding exists; never add a second `[HotfixLifecycle(typeof(IGameSessionLifecycle))]` class.
4. Read [references/session-lifecycle-shapes.md](references/session-lifecycle-shapes.md) before designing event behavior.
5. Write down the intended policy for each event before changing code:
   - disconnected inside the resume window
   - expired after the resume window
   - control-session versus realtime-session loss
   - stale events for a superseded session
   - explicit termination, if requested
6. Use stable business actors or application services as the durable authority. Read current ownership before mutating it.
7. Verify that the lifecycle event's session ID still matches the actor's current control or realtime session ID. Treat a stale event as an idempotent no-op.
8. Keep disconnect handling compatible with reconnection. Perform irreversible room, match, presence, or actor cleanup on expiration unless the product explicitly requires an earlier transition.
9. Reuse established constructor injection, generated actor selectors, logging, cancellation, and error-handling conventions.
10. Build the Hotfix project and run focused lifecycle tests, including disconnect, expiration, reconnection, stale-event, and control/realtime cases relevant to the change.

## Required Binding

Use the project's current API shape. A current binding has one class annotated with:

```csharp
[HotfixLifecycle(typeof(IGameSessionLifecycle))]
internal sealed class GameSessionLifecycle
```

Its handlers accept `HotfixLifecycleCall<GameSessionDisconnectedRequest>` and `HotfixLifecycleCall<GameSessionExpiredRequest>`. Do not expose this contract in Shared or name the class as an RPC `*Service`.

## Lifecycle Boundaries

- An RPC Session is one live connection. A Game Session can outlive that connection and resume during its recovery window. A product Player Session is a separate domain concept.
- `SessionDisconnectedAsync` means the connection was lost but the Game Session remains resumable. Avoid durable removal or user-visible offline transitions that would make recovery inconsistent.
- `SessionExpiredAsync` means the recovery window ended and Lakona removed framework session state. Perform durable product cleanup here, subject to the product's control/realtime policy.
- Explicit administrative or product termination uses `ILakonaGameServer.TerminateSessionAsync`. Do not simulate it by invoking a lifecycle handler or closing a raw RPC connection.
- Control and realtime sessions are independent. Do not clear both merely because one expired unless product policy explicitly couples them.

## Safety Rules

- Do not subscribe directly to `RpcSession.Disconnected`, depend on endpoint names, add an App-to-Hotfix bridge, or duplicate the framework lifecycle contract.
- Do not store callbacks, transports, actor references, or durable game state in session items. Session items are a small scalar cache and lifecycle calls expose only an immutable snapshot.
- Make cleanup idempotent. Missing actors, already-left rooms, repeated events, and superseded sessions need deliberate behavior.
- Do not swallow cancellation or concrete cleanup failures. Follow project logging policy and avoid reporting success for incomplete durable cleanup.
- Keep Hotfix lifecycle methods thin. Route business mutations through their owning actor or application service.

## Validation

At minimum, build the discovered Hotfix project. Run or add focused tests for:

- disconnect retaining resumable state
- expiration performing the intended durable cleanup
- reconnection before expiration
- a stale event not clearing a replacement session
- independent control and realtime session handling
- repeated cleanup and missing-state behavior
- explicit termination remaining separate from disconnect and expiration

Report the policy implemented, files changed, validation commands, and any product choice that remains unresolved. Compilation validates the binding shape; only behavioral evidence validates cleanup semantics.
