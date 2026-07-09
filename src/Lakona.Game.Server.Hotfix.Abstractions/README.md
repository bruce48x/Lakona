# Lakona.Game.Server.Hotfix.Abstractions

Stable attributes, lifecycle calls, and result DTOs for Lakona.Game server Hotfix behaviors.

This package is intentionally small so stable model projects, hotfix projects, runtime packages, and source generators can share the same metadata without depending on Lakona.Game server hosting internals.

## Metadata

- `[HotfixState]` marks stable partial actor types that can receive generated friend accessors.
- `[HotfixBehaviorOf]` binds a static partial Hotfix behavior class to the stable actor type it extends.
- `[FriendOf]` declares that a Hotfix behavior is intended to use generated friend accessors for a stable actor type.
- `[HotfixService]` marks the single hotfix implementation for a generated RPC service contract.
- `HotfixMethodKey`, `HotfixSnapshot`, and `HotfixReloadResult` describe loaded method identity and reload outcomes.
- `IHotfixRequiredServiceContracts` is emitted by generated server apps so the runtime can fail reloads when a required RPC service has zero or multiple hotfix implementations.
- `LakonaTimer`, `TimerId`, and `TimerTick<TArgs>` define the hotfix-safe timer surface used by timer callbacks.

`[FriendOf]` is metadata for the hotfix model and tooling. It is not an access-control mechanism; generated accessors are normal public members on the stable type in the first implementation.

Stable App assemblies own actor identity, serialized state, persistence schema, DTOs, RPC contracts, and transport contracts. Hotfix assemblies own replaceable behavior. Public extension methods in `[HotfixBehaviorOf]` classes are the actor API exposed through generated selectors and actor refs.

## Hotfix Startup

Hotfix startup uses a static convention class:

```csharp
public static class HotfixStartup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<BattleRuntimeTimers>();
    }

    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup(
            "matchmaking",
            static _ => ActorStartupPlan.Create<MatchmakingActor>(ActorId.From("default")));
    }
}
```

Both methods are optional, but when present they must be public static void
methods with exactly one supported parameter. Startup methods are declaration
surfaces; the runtime does not construct `HotfixStartup`.

## Timers

Timers are created by explicit hotfix services or actor behavior methods and
resolved against the current hotfix callback table:

```csharp
public static async ValueTask CreateBattleTimerAsync(CancellationToken cancellationToken)
{
    await LakonaTimer.CreatePeriodicTimerAsync<BattleTimers, BattleTick>(
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(50),
        nameof(BattleTimers.TickAsync),
        new BattleTick("default"),
        cancellationToken);
}

public sealed class BattleTimers
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
Shutdown code should destroy timers with noncancelable cleanup when the
timer must not leak after a canceled stop token.
