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

## Behavior Callback

Declare the callback on its owning Behavior in Hotfix:

```csharp
[ActorTimer]
private ValueTask TickAsync(MatchmakingActor self, TimerTick<MatchmakingTimerArgs> tick)
{
    return RunTickAsync(self, new MatchmakingTickRequest
    {
        ObservedAtUtc = DateTime.UtcNow
    }, CancellationToken.None);
}
```

The callback returns `ValueTask` and takes exactly the owning Actor and tick.
It runs in that activation's mailbox; use `self` directly, without a forwarding
Actor call. Private methods are supported and excluded from RPC generation.
Use constructor injection for immutable current-generation dependencies.

## Create A One-Shot Timer

Create from the owning Actor turn (Behavior method, lifecycle hook, or timer
callback) while its Hotfix execution scope is active:

```csharp
var timerId = self.CreateOnceTimer(
    static (RoomBehavior behavior) => behavior.ExpireAsync,
    TimeSpan.FromMinutes(5),
    new RoomExpiryTimerArgs { RoomId = roomId.Value });
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

self.MatchmakingTimerId = self.CreatePeriodicTimer(
    static (MatchmakingBehavior behavior) => behavior.TickAsync,
    TimeSpan.Zero,
    TimeSpan.FromSeconds(1),
    new MatchmakingTimerArgs { OwnerActorId = self.Context.Id.Value });
```

The due time must not be negative and the period must be greater than zero.
Choose a period from product behavior and load limits, not from an arbitrary
sample value.

## Destroy An Owned Timer

Clear stable ownership before cancellation:

```csharp
var timerId = self.MatchmakingTimerId;
self.MatchmakingTimerId = default;
if (!timerId.IsValid)
{
    return;
}

self.DestroyTimer(timerId);
```

Clearing first keeps cleanup idempotent and prevents later code from treating a
timer being destroyed as active. Creation and cancellation do not take a token;
callbacks which have started run to completion.

## Actor-Owned Lifecycle

Start timers from the owning Actor's Behavior or `[ActorStart]` hook. Activation
shutdown cancels timers automatically, including pending mailbox work. A new
activation with the same ID never receives an old activation's timer. Store a
`TimerId` only for early cancellation, correlation, or duplicate prevention;
an `[ActorStop]` hook solely to destroy timers is unnecessary.

Cancellation prevents pending and future callbacks without interrupting one already
running. A callback can destroy its own timer without deadlocking.

Do not assume an actor call creates a missing owner. Actor hosting or startup
registration must establish the owner independently.

## Reload And Failure Semantics

The scheduler stores stable callback identity rather than retaining a Hotfix
delegate. After reload, a later tick resolves the matching callback on the new
generation. Renaming or removing an active callback therefore requires a
compatible lifecycle or migration decision; do not casually rename callback
methods while timers using them may still exist.

Ticks provide no cancellation token. If downstream work uses an application-owned
cancellation token, propagate it deliberately; do not infer one from timer destruction.
Let failures reach the project's timer diagnostics or
handle them where a concrete retry, disable, or state-repair policy exists. Do
not swallow missing actors, serialization errors, or callback exceptions as
successful ticks.

## Scheduling And Admission

Each timer has at most one pending or running execution. After actual callback
completion, its next due time is `max(actualStart + period, completion)`; elapsed
historical periods do not produce a tick backlog. Accepted work waits under
scheduler or mailbox capacity pressure. Reaching `MaxActiveTimers` rejects new
creation. Failures are reported without retrying that execution; a periodic
timer continues its next round. These guarantees are process-local, and do not
guarantee exactly-once business side effects or recovery after process loss.
