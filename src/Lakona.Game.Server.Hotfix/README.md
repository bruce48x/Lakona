# Lakona.Game.Server.Hotfix

Runtime loader and dispatch infrastructure for server-side Lakona.Game hotfix assemblies.

This package keeps reload mechanics separate from actor runtime, sessions, transports, and gameplay code.

## Design model

Lakona.Game hotfix separates stable state from replaceable logic:

```txt
stable actor or state object + reloadable static system methods
```

Actors, room loops, timers, persistence, RPC contracts, transports, and long-lived mutable state stay in stable assemblies. Hotfix assemblies contain stateless business rules that operate on stable state objects. A reload replaces the runtime dispatch table; it does not replace existing actor or state instances.

Hotfix systems should return stable DTOs that describe what happened. Stable runtime code should perform side effects such as persistence writes, leaderboard updates, session cleanup, logging, and network pushes.

Reload uses next-entry semantics: a method already executing keeps the version it resolved, while the next dispatch call sees the new table after a successful reload. If reload fails, the previous dispatch table remains active.

## Server hotfix flow

Stable code owns state:

```csharp
[HotfixState]
public partial class PlayerActor : Actor
{
    private int level;
    private int exp;
}
```

Hotfix code owns behavior:

```csharp
[FriendOf(typeof(PlayerActor))]
[HotfixSystemOf(typeof(PlayerActor))]
public static class PlayerActorSystem
{
    public static void AddExp(this PlayerActor self, int amount)
    {
        var exp = self.__hotfix_exp();
    }
}
```

Reload with `IHotfixManager.ReloadAsync()`. Reload failure keeps the previous dispatch table active.

Use `AddLakonaGameHotfix(...)` to register a source such as `CurrentDirectoryHotfixAssemblySource`, and pass stable assembly names as shared assemblies so hotfix systems operate on the same state types as the running server. `AddLakonaGameHotfixFileWatcher(...)` can be added when a host should reload after hotfix DLL changes.

## First-version boundaries

The first implementation uses one process-global dispatch table. Treat it as one hotfix domain per server process; do not register unrelated hotfix managers that should carry independent behavior in the same process.

Generated friend accessors are public members on `[HotfixState]` partial types because the hotfix assembly must be able to call them across an assembly boundary. `[FriendOf]` is metadata and convention for hotfix systems, not a CLR security boundary. Only mark stable state types where exposing generated `__hotfix_` accessors is acceptable, and keep sensitive runtime internals outside those state types.

Full generated call wrappers are staged. The current runtime supports generated accessors and `HotfixDispatch.Invoke(...)`; stable code should provide explicit wrapper methods such as `TickWithHotfix(...)` or `SettleMatch(...)` at hotfix entry points.

State shape changes, protocol changes, serializer changes, persistent schema changes, and actor runtime changes are not hotfixes. Deploy or migrate stable assemblies for those changes.
