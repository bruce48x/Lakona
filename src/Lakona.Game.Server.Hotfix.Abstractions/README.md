# Lakona.Game.Server.Hotfix.Abstractions

Stable attributes, lifecycle calls, result DTOs, and compiler support for
Lakona.Game server Hotfix behaviors.

This internal assembly stays separate so stable model projects, hotfix
projects, and the server runtime can share the same metadata and type identity
without depending on hosting internals. It is not published independently:
the `Lakona.Game.Server` NuGet package carries this assembly and the matching
compiler extension.

## Metadata

- `[HotfixState]` marks stable partial actor types that can receive generated friend accessors.
- `[HotfixBehaviorOf]` binds a sealed partial generation-scoped behavior class to the stable actor type it operates on.
- `[FriendOf]` declares that a Hotfix behavior is intended to use generated friend accessors for a stable actor type.
- `[HotfixService]` marks the single hotfix implementation for a generated RPC service contract.
- `[HotfixComponent]` marks a dependency-only helper that is automatically registered once per hotfix generation.
- `HotfixMethodKey`, `HotfixSnapshot`, and `HotfixReloadResult` describe loaded method identity and reload outcomes.
- `HotfixHttpEndpointDescriptor` carries the stable, process-local HTTP route
  manifest without exposing Hotfix load-context types to the server host.
- `IHotfixRequiredServiceContracts` is emitted by generated server apps so the runtime can fail reloads when a required RPC service has zero or multiple hotfix implementations.
- `[HotfixTimer]`, `LakonaTimer`, `HotfixTimerEntry<TArgs>`, `TimerId`, and `TimerTick<TArgs>` define the hotfix-safe timer surface.

`[FriendOf]` is metadata for the hotfix model and tooling. It is not an access-control mechanism; generated accessors are normal public members on the stable type in the first implementation.

Stable App assemblies own actor identity, serialized state, persistence schema, DTOs, RPC contracts, and transport contracts. Hotfix assemblies own replaceable behavior. Public instance methods in `[HotfixBehaviorOf]` classes are called through generated actor APIs with direct static selectors such as `static behavior => behavior.JoinAsync`.

## Hotfix Startup

Hotfix startup uses explicit attributes for the startup type and optional
configuration methods. Generation-scoped helper services use
`[HotfixComponent]`; they do not require manual startup registration:

```csharp
[HotfixStartup]
public static class GameHotfixStartup
{
    [HotfixConfigureActors]
    public static void Actors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>(
            static context => SelectStableHash(context.Candidates, context.Key.Value));
    }
}

[HotfixComponent]
public sealed class BattleNotifier
{
    private readonly ILogger<BattleNotifier> logger;

    public BattleNotifier(ILogger<BattleNotifier> logger)
    {
        this.logger = logger;
    }
}
```

Both methods are optional, but when present they must be public static void
methods with exactly one supported parameter. Startup methods are declaration
surfaces; the runtime does not construct the `[HotfixStartup]` type.
`TKey` provides routing affinity to the selector; it is not the replica's actor
id. The selector is fixed by registration and must return one offered ready
candidate.

## Timers

Timers are created by explicit hotfix services or actor behavior methods and
resolved against the current hotfix callback table:

```csharp
public async ValueTask CreateBattleTimerAsync(CancellationToken cancellationToken)
{
    await LakonaTimer.CreatePeriodicTimerAsync(
        static (BattleTimers callbacks) => callbacks.TickAsync,
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(50),
        new BattleTick("default"),
        cancellationToken);
}

[HotfixTimer]
public sealed partial class BattleTimers
{
    public ValueTask TickAsync(TimerTick<BattleTick> tick)
    {
        return default;
    }
}

// Declared in the stable App or Contracts assembly.
public sealed record BattleTick(string QueueId);
```

Use `LakonaTimer.CreateOnceTimerAsync(static (TimerModule callbacks) => callbacks.Method,
dueTime, args, cancellationToken)` for one-shot work and
`LakonaTimer.CreatePeriodicTimerAsync(static (TimerModule callbacks) => callbacks.Method,
dueTime, period, args, cancellationToken)` for periodic work.
Shutdown code should destroy timers with noncancelable cleanup when the
timer must not leak after a canceled stop token.
