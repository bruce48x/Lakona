# Lakona Session Lifecycle Shapes

Use the installed Lakona version and neighboring project code as the API authority. The examples below describe current concepts and safety invariants, not a namespace template.

## Vocabulary

| Concept | Lifetime | Owner |
| --- | --- | --- |
| RPC Session | One live transport connection | RPC runtime |
| Game Session | Resumable server session across connections | Lakona Game Server |
| Player Session | Product login or character presence | Game domain |

Do not infer that these lifetimes begin or end together.

## Lifecycle Binding

```csharp
[HotfixLifecycle(typeof(IGameSessionLifecycle))]
internal sealed class GameSessionLifecycle
{
    public ValueTask SessionDisconnectedAsync(
        HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
    {
        // Retain resumable domain state unless product policy says otherwise.
        return default;
    }

    public ValueTask SessionExpiredAsync(
        HotfixLifecycleCall<GameSessionExpiredRequest> call)
    {
        // Perform product cleanup through the business owner.
        return default;
    }
}
```

The request identifies framework state through `OwnerKey`, `SessionId`, and `ConnectionId`. Do not assume it contains arbitrary product context. Resolve product ownership through established actor keys or application services.

## Default Policy Matrix

| Event | Framework meaning | Typical product action |
| --- | --- | --- |
| Disconnected | Connection lost; Game Session retained | Keep room and actor state; optionally mark a temporary connection condition |
| Reconnected | New connection attached inside recovery window | Restore connection-specific bindings without recreating durable identity |
| Expired | Recovery window ended; framework session removed | Clear matching session ownership and leave rooms, matchmaking, or presence as product policy requires |
| Explicit termination | Product or administrator ends a session | Call `ILakonaGameServer.TerminateSessionAsync`; notify best-effort if appropriate |

The project may choose a different visible-presence policy, but it must state and test that choice. Avoid presence flicker by default when short disconnects are recoverable.

## Stale-Event Protection

A lifecycle event can race with reconnection or replacement. Before clearing domain state:

1. Resolve the stable business actor from `OwnerKey` or the project's established mapping.
2. Read its current control and realtime session ownership.
3. Compare the event `SessionId` with the specific current session slot.
4. Mutate only the matching slot and its dependent state.
5. Return successfully without mutation when the event is stale or already handled.

Conceptually:

```csharp
var snapshot = await userActor.GetSessionSnapshotAsync(cancellationToken);

if (snapshot.ControlSessionId == call.Request.SessionId)
{
    await userActor.ClearControlSessionAsync(call.Request.SessionId, cancellationToken);
}
else if (snapshot.RealtimeSessionId == call.Request.SessionId)
{
    await userActor.ClearRealtimeSessionAsync(call.Request.SessionId, cancellationToken);
}
```

Use the project's generated actor calls and actual key types. Pass the expected session ID into the mutation when possible so the actor enforces the compare-and-clear invariant atomically.

## Control And Realtime Independence

Control and realtime sessions can reconnect and expire independently. Define the product effects separately:

- Control expiration may end login, lobby, or command ownership.
- Realtime expiration may remove match participation or realtime presence.
- One event should not clear the other session slot merely for convenience.
- Cross-termination or shared cleanup requires explicit product policy and tests.

## Explicit Termination

Use the stable server API when the product intentionally terminates a Game Session:

```csharp
await gameServer.TerminateSessionAsync(sessionKey, cancellationToken);
```

Do not call lifecycle handlers directly and do not treat a raw connection close as equivalent. A client notice, when used, is best-effort and must not become the durable authority for termination.

## State, Cancellation, And Failures

- Use session items only for small scalar values supported by the current API, such as strings, integers, and booleans.
- Lifecycle calls expose an immutable item snapshot; they are not a durable database.
- Keep durable business state in actors or the project's application services.
- Cleanup that must survive request cancellation may deliberately use `CancellationToken.None` when established project policy requires it.
- Do not swallow `OperationCanceledException` accidentally or convert concrete cleanup failures into silent success.
- Make repeated expiration and missing-state paths safe and observable according to repository conventions.

Validate failure paths as well as the happy path: unavailable actor, already-left room, partial cleanup, repeated event, and stale replacement event.
