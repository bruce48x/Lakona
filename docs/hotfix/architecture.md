# Hotfix Architecture

Lakona hotfix lets game behavior reload without replacing the stable server
host. Stable `Server.App` owns actor state types, contracts, host wiring, and
runtime integration. Reloadable `Server.Hotfix` owns services, actor behavior
methods, actor lifecycle hooks, timer callbacks, and business rules.

## Boundaries

| Layer | Owns |
| --- | --- |
| `Shared` | RPC contracts, callback contracts, DTOs, named contract ids |
| `Server.App` | actor state shells, host configuration, stable runtime services |
| `Server.Hotfix` | service implementations, `[HotfixBehaviorOf]` actor methods, `[ActorStart]`, `[ActorStop]`, timer callbacks |

Hotfix code is loaded through `HotfixManager`. Reload validation builds a
dispatch table, verifies required contracts, creates a candidate service
provider, and rolls back candidate-created actors if activation fails.

The stable host and collectible Hotfix load context share framework assemblies,
the entry assembly, and the assemblies that own generated required service
contracts. Assembly identity comes from the discovered contract `Type` objects;
the runtime must not guess project names such as `Shared`, `Server.App`, or
`State.Contracts`. This keeps custom contract assembly names valid while
preventing duplicate type identities across load contexts.

## Actor Lifecycle

Use explicit actor lifecycle attributes:

```csharp
[ActorStart]
public ValueTask StartAsync(MatchmakingActor self, ActorStartCall call)
{
    return self.StartTimerAsync(new MatchmakingTimerStartRequest(), call.CancellationToken);
}

[ActorStop]
public ValueTask StopAsync(MatchmakingActor self, ActorStopCall call)
{
    return self.StopTimerAsync(new MatchmakingTimerStopRequest(), call.CleanupCancellationToken);
}
```

`Lakona:StartupActors` selects which named actor startup declarations are
activated on a node. Node placement and route choice belong in code and actor
route policy, not in a separate component model.

## Timers

Hotfix timers use `LakonaTimer` from an active hotfix execution scope:

```csharp
await LakonaTimer.CreatePeriodicTimerAsync(
    MatchmakingTimerCallbacks.Entries.TickAsync,
    TimeSpan.Zero,
    TimeSpan.FromSeconds(1),
    new MatchmakingTimerArgs(),
    call.CancellationToken);
```

Timer callbacks should enter generated actor selectors or application services.
They should not hold transport callbacks, session callback objects, or mutable
global game state.
