# Lakona.Game.Server.Hotfix.Abstractions

Stable attributes and result contracts for Lakona.Game server Hotfix behaviors.

This package is intentionally small so stable model projects, hotfix projects, runtime packages, and source generators can share the same metadata without depending on Lakona.Game server hosting internals.

## Contracts

- `[HotfixState]` marks stable partial actor types that can receive generated friend accessors.
- `[HotfixBehaviorOf]` binds a static partial Hotfix behavior class to the stable actor type it extends.
- `[FriendOf]` declares that a Hotfix behavior is intended to use generated friend accessors for a stable actor type.
- `[HotfixService]` marks the single hotfix implementation for a generated RPC service contract.
- `HotfixMethodKey`, `HotfixSnapshot`, and `HotfixReloadResult` describe loaded method identity and reload outcomes.
- `IHotfixRequiredServiceContracts` is emitted by generated server apps so the runtime can fail reloads when a required RPC service has zero or multiple hotfix implementations.
- `LakonaTimer`, `TimerId`, and `TimerTick<TArgs>` define the hotfix-safe timer surface used by feature lifecycle methods and timer callbacks.

`[FriendOf]` is metadata for the hotfix model and tooling. It is not an access-control mechanism; generated accessors are normal public members on the stable type in the first implementation.

Keep actor identity, serialized state, persistence schema, RPC contracts, and transport contracts outside the hotfix assembly.

## Timers

Feature-owned timers are created from feature `StartAsync`, stored in feature
state, and destroyed from `StopAsync`:

```csharp
public static async ValueTask StartAsync(HotfixFeatureStartCall call)
{
    var timerId = await LakonaTimer.CreatePeriodicTimerAsync<BattleTimers, BattleTick>(
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(50),
        nameof(BattleTimers.TickAsync),
        new BattleTick("default"),
        call.CancellationToken);

    call.State.Items["battle.timer"] = timerId;
}

public static async ValueTask StopAsync(HotfixFeatureStopCall call)
{
    if (call.State.Items.TryGetValue("battle.timer", out var value) &&
        value is TimerId timerId)
    {
        await LakonaTimer.DestroyTimerAsync(timerId, call.CancellationToken);
    }

    call.State.Items.Remove("battle.timer");
}

public static class BattleTimers
{
    public static ValueTask TickAsync(TimerTick<BattleTick> tick)
    {
        return default;
    }
}

public sealed record BattleTick(string QueueId);
```

Use `LakonaTimer.CreateOnceTimerAsync<TCallback, TArgs>(dueTime,
nameof(TCallback.Method), args, cancellationToken)` for one-shot work and
`LakonaTimer.CreatePeriodicTimerAsync<TCallback, TArgs>(dueTime, period,
nameof(TCallback.Method), args, cancellationToken)` for periodic work.
