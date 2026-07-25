# Lakona.Game.Server.Hotfix

Runtime loader and dispatch infrastructure for server-side Lakona.Game hotfix assemblies.

This package keeps reload mechanics separate from actor runtime, sessions, transports, and gameplay code.

## Design model

Lakona.Game hotfix separates stable actor state from replaceable logic:

```txt
stable Actor<TKey> + reloadable generation-scoped module instances
```

Actors, room loops, timers, persistence, RPC contracts, transports, and
long-lived mutable state stay in stable assemblies. Hotfix assemblies contain
stateless business rules that operate on stable actor instances. A reload
replaces the runtime dispatch table; it does not replace existing actor
instances.

Public instance methods in sealed partial `[HotfixBehaviorOf]` classes are the actor API.
Stable App assemblies own actor state, identity, and DTOs. Hotfix assemblies own
behavior-derived selectors, refs, and wrappers that expose those methods to
services and lifecycle code.

Hotfix behaviors should return stable DTOs that describe what happened. Stable runtime code should perform side effects such as persistence writes, leaderboard updates, session cleanup, logging, and network pushes.

Reload uses next-entry semantics: a method already executing keeps the version it resolved, while the next dispatch call sees the new table after a successful reload. If reload fails, the previous dispatch table remains active.

## Timers

Actor-owned timers use the stable `LakonaTimer` facade from
`Lakona.Game.Server.Hotfix.Abstractions`. Create timers in `[ActorStart]`,
store the returned `TimerId` in stable actor state, and destroy them in
`[ActorStop]`:

```csharp
[HotfixState]
public sealed partial class BattleActor : Actor<ActorId>
{
    internal TimerId BattleTimerId;
}

[FriendOf(typeof(BattleActor))]
[HotfixBehaviorOf(typeof(BattleActor))]
public sealed partial class BattleBehavior
{
    [ActorStart]
    public async ValueTask StartAsync(BattleActor self, ActorStartCall call)
    {
        self.BattleTimerId = await LakonaTimer.CreatePeriodicTimerAsync(
            static (BattleTimers callbacks) => callbacks.TickAsync,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50),
            new BattleTick(call.ActorId.ToString()!),
            call.CancellationToken);
    }

    [ActorStop]
    public async ValueTask StopAsync(BattleActor self, ActorStopCall call)
    {
        await LakonaTimer.DestroyTimerAsync(
            self.BattleTimerId,
            call.CleanupCancellationToken);
    }
}
```

Timer callbacks are instance methods referenced through direct static selectors:

```csharp
[HotfixTimer]
public sealed partial class BattleTimers
{
    public ValueTask TickAsync(TimerTick<BattleTick> tick)
    {
        return default;
    }
}
```

Use `ActorStopCall.CleanupCancellationToken` for timer destruction when a
canceled stop token must not leave a timer registered.

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
public sealed partial class PlayerBehavior
{
    public void AddExp(PlayerActor self, int amount)
    {
        var exp = self.__hotfix_exp();
    }
}
```

Each attributed hotfix module is activated once per published generation. It
may capture dependencies only through private readonly constructor-assigned
fields or private get-only properties. Counters, caches, collections, events,
and other mutable business state belong in stable actors or stable runtime
services; analyzer `LKNHOTFIX032` rejects them in hotfix modules.

Dependency-only collaboration services use `[HotfixComponent]`. Source
generation registers each component once in the generation provider, which
owns its activation and disposal. Hotfix projects are closed to unclassified
classes: `LKNHOTFIX037` requires a hotfix role or a move to a stable
assembly, while `LKNHOTFIX038` keeps unannotated static utilities free of
hidden static state. Request, result, timer-argument, and persistence data
types belong in stable App or Contracts assemblies.

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

Generated server apps discover `[RpcService]` contracts at build time and emit stable service proxies plus one service-scoped call context such as `ChatServiceCall<TRequest>`. The generated call exposes the callback contract as a strongly typed `Callback` property without requiring every handler to repeat that callback type. Hotfix assemblies implement those contracts with exactly one `[HotfixService(typeof(IMyService))]` implementation type per generated service contract. Instance methods are activated with the current hotfix service provider and use the generated service call argument; static methods remain supported and may use raw request DTO parameters for allocation-sensitive paths. Reload validation rejects missing or duplicate required service implementations before publishing a new dispatch table.

Hotfix-owned `[LakonaHttpService]` classes declare routes and handlers together.
Handlers use `[LakonaHttpEndpoint(method, route)]`, accept `LakonaHttpCall`, and
return `ValueTask<LakonaHttpResponse>`. The initial generation freezes the
process-local route manifest and the stable host assigns internal endpoint
slots; application code does not declare numeric HTTP method ids. Later
generations must preserve the manifest while replacing handler behavior. The
stable ASP.NET host owns sockets, admission, cooperative deadlines, and
response writing; Hotfix treats snapshot values as read-only, observes
cancellation, and never receives `HttpContext`.

State shape changes, protocol changes, serializer changes, persistent schema changes, and actor runtime changes are not hotfixes. Deploy or migrate stable assemblies for those changes.
