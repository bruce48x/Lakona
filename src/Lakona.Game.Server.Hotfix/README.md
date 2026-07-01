# Lakona.Game.Server.Hotfix

Runtime loader and dispatch infrastructure for server-side Lakona.Game hotfix assemblies.

This package keeps reload mechanics separate from actor runtime, sessions, transports, and gameplay code.

## Design model

Lakona.Game hotfix separates stable actor state from replaceable logic:

```txt
stable Actor<TKey> + reloadable static partial behavior methods
```

Actors, room loops, timers, persistence, RPC contracts, transports, and
long-lived mutable state stay in stable assemblies. Hotfix assemblies contain
stateless business rules that operate on stable actor instances. A reload
replaces the runtime dispatch table; it does not replace existing actor
instances.

Hotfix behaviors should return stable DTOs that describe what happened. Stable runtime code should perform side effects such as persistence writes, leaderboard updates, session cleanup, logging, and network pushes.

Reload uses next-entry semantics: a method already executing keeps the version it resolved, while the next dispatch call sees the new table after a successful reload. If reload fails, the previous dispatch table remains active.

## Timers

Feature-owned timers use the stable `LakonaTimer` facade from
`Lakona.Game.Server.Hotfix.Abstractions`. Create timers in feature `StartAsync`,
store the returned `TimerId` in feature state, and destroy them in `StopAsync`:

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
```

Timer callbacks are static methods referenced by name:

```csharp
public static class BattleTimers
{
    public static ValueTask TickAsync(TimerTick<BattleTick> tick)
    {
        return default;
    }
}
```

## Server hotfix flow

Stable code owns state:

```csharp
[HotfixState]
public sealed partial class PlayerActor : Actor<PlayerId>
{
    private int level;
    private int exp;
}
```

Hotfix code owns behavior:

```csharp
[FriendOf(typeof(PlayerActor))]
[HotfixBehaviorOf(typeof(PlayerActor))]
public static partial class PlayerBehavior
{
    public static void AddExp(this PlayerActor self, int amount)
    {
        var exp = self.__hotfix_exp();
    }
}
```

Reload with `IHotfixManager.ReloadAsync()`. Reload failure keeps the previous dispatch table active.

Use `AddLakonaGameHotfix(...)` to register a source such as `CurrentDirectoryHotfixAssemblySource`, and pass stable assembly names as shared assemblies so Hotfix behaviors operate on the same stable actor types and actor instances as the running server. `AddLakonaGameHotfixFileWatcher(...)` can be added when a host should reload after hotfix DLL changes.

## Loader safety contract

`HotfixManager` must be the only component that loads hotfix assemblies. Hosts must not call `Assembly.LoadFrom` on files in the hotfix directory. Reload reads the main hotfix DLL, adjacent PDB, and managed dependency DLLs into memory via stream loading before loading them into a collectible `AssemblyLoadContext`, validates shared type identity, and publishes the dispatch table only after validation succeeds. Native dependencies continue to use path-based loading.

Use version-pointer deployment for production:

```txt
hotfix/current.txt
hotfix/versions/<version>/Server.Hotfix.dll
hotfix/versions/<version>/Server.Hotfix.pdb
```

The pointer should change only after the version directory is fully written.

## First-version boundaries

The first implementation uses one process-global dispatch table. Treat it as one hotfix domain per server process; do not register unrelated hotfix managers that should carry independent behavior in the same process.

Generated friend accessors are public members on `[HotfixState]` partial actor
types because the hotfix assembly must be able to call them across an assembly
boundary. `[FriendOf]` is metadata and convention for Hotfix behaviors, not a
CLR security boundary. Only mark stable actor types where exposing generated
`__hotfix_` accessors is acceptable, and keep sensitive runtime internals
outside those actors.

Generated server apps discover `[RpcService]` contracts at build time and emit stable service proxies that call hotfix methods through `HotfixServiceCall<TRequest>` or `HotfixServiceCall<TRequest, TCallback>`. Hotfix assemblies implement those contracts with exactly one `[HotfixService(typeof(IMyService))]` implementation type per generated service contract. Instance methods are activated with the current hotfix service provider and must use call-wrapper arguments; static methods remain supported and may use raw request DTO parameters for allocation-sensitive paths. Reload validation rejects missing or duplicate required service implementations before publishing a new dispatch table.

State shape changes, protocol changes, serializer changes, persistent schema changes, and actor runtime changes are not hotfixes. Deploy or migrate stable assemblies for those changes.
