# Lakona Timer Shapes

Use repository evidence as the final authority. Lakona timers are framework-
owned schedules whose callback is resolved on the active Hotfix generation.

## Stable Timer Arguments

Define arguments in the stable App assembly so a stored schedule does not
depend on a collectible Hotfix type:

```csharp
public sealed class MatchmakingTimerArgs
{
    public string OwnerActorId { get; init; } = string.Empty;
}
```

Keep the root type non-generic. Use public properties rather than public
fields. Supported shapes include concrete DTOs, enums, scalar values, arrays,
and `List<T>` with supported element types.

Avoid:

- `object`, interface, abstract, or delegate-typed members
- framework service, actor, callback, task, or cancellation-token references
- reference cycles or unbounded object graphs
- generic root argument types
- types that cannot round-trip through the project's timer serializer

The current timer codec performs a System.Text.Json-based round-trip check and
limits nested depth. Prefer small identity and policy values; load current state
from its owner during the callback.

## Callback Module

Declare the callback in Hotfix:

```csharp
[HotfixTimer]
public sealed partial class MatchmakingTimerCallbacks
{
    private readonly ActorAccess _actors;

    public MatchmakingTimerCallbacks(ActorAccess actors)
    {
        _actors = actors;
    }

    public ValueTask TickAsync(TimerTick<MatchmakingTimerArgs> tick)
    {
        return _actors
            .Local<MatchmakingActor>(new MatchmakingQueueId(tick.Args.OwnerActorId))
            .PostAsync(
                static behavior => behavior.RunTickAsync,
                new MatchmakingTickRequest
                {
                    ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
                },
                tick.CancellationToken);
    }
}
```

Use constructor injection for current-generation dependencies. Keep callback
methods public and instance-based so the Hotfix generator can emit typed timer
entries. Use `tick.DueAtUtc` when the scheduled instant matters and
`tick.ObservedAtUtc` when actual scheduler observation matters.

Use `Route` instead of `Local` unless the timer's ownership model proves that
the target actor is hosted on the current node. A timer that stores an exact
local owner actor ID and is destroyed with that actor can normally use `Local`.

## Create A One-Shot Timer

Create from a service, actor behavior, lifecycle hook, or timer callback while
its Hotfix execution scope is active:

```csharp
var timerId = await LakonaTimer.CreateOnceTimerAsync(
    static (RoomTimerCallbacks callbacks) => callbacks.ExpireAsync,
    TimeSpan.FromMinutes(5),
    new RoomExpiryTimerArgs { RoomId = roomId.Value },
    cancellationToken);
```

The due time must not be negative. Store the returned `TimerId` only when the
owner may cancel the one-shot before it fires or needs to correlate it.

## Create A Periodic Timer

Prevent duplicate ownership before creating:

```csharp
if (self.MatchmakingTimerId.IsValid)
{
    return;
}

self.MatchmakingTimerId = await LakonaTimer.CreatePeriodicTimerAsync(
    static (MatchmakingTimerCallbacks callbacks) => callbacks.TickAsync,
    TimeSpan.Zero,
    TimeSpan.FromSeconds(1),
    new MatchmakingTimerArgs { OwnerActorId = self.Context.Id.Value },
    cancellationToken);
```

The due time must not be negative and the period must be greater than zero.
Choose a period from product behavior and load limits, not from an arbitrary
sample value.

## Destroy An Owned Timer

Clear stable ownership before awaiting destruction:

```csharp
var timerId = self.MatchmakingTimerId;
self.MatchmakingTimerId = default;
if (!timerId.IsValid)
{
    return;
}

await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None);
```

Clearing first keeps cleanup idempotent and prevents later code from treating a
timer being destroyed as active. Use the caller's token for ordinary user-
requested cancellation; use `CancellationToken.None` when actor shutdown must
finish cleanup despite a canceled stop request.

## Actor-Owned Lifecycle

Store the timer ID on the stable actor and start or stop it from reloadable
lifecycle hooks:

```csharp
[ActorStart]
public ValueTask StartAsync(MatchmakingActor self, ActorStartCall call)
{
    return EnsureTimerAsync(self, call.CancellationToken);
}

[ActorStop]
public ValueTask StopAsync(MatchmakingActor self, ActorStopCall call)
{
    return DestroyTimerAsync(self);
}
```

Do not assume an actor call creates a missing owner. Actor hosting or startup
registration must establish the owner independently.

## Reload And Failure Semantics

The scheduler stores stable callback identity rather than retaining a Hotfix
delegate. After reload, a later tick resolves the matching callback on the new
generation. Renaming or removing an active callback therefore requires a
compatible lifecycle or migration decision; do not casually rename callback
methods while timers using them may still exist.

Propagate cancellation. Let failures reach the project's timer diagnostics or
handle them where a concrete retry, disable, or state-repair policy exists. Do
not swallow missing actors, serialization errors, or callback exceptions as
successful ticks.
