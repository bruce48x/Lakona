# Hotfix Architecture

## Overview

Lakona.Game's hotfix system allows business logic to be updated without restarting
the server process. This document describes the architecture, naming conventions,
and design rationale.

## Stable State + Replaceable Logic

The foundational design principle:

```
Stable runtime state    →   lives in Server.App, survives hot reloads
Replaceable logic       →   lives in Server.Hotfix, loaded via AssemblyLoadContext
```

When a Hotfix assembly is reloaded:

1. The old `AssemblyLoadContext` is unloaded.
2. A new `AssemblyLoadContext` loads the updated assembly.
3. The dispatch table is rebuilt by scanning `[HotfixSystemOf]` types.
4. All actor state, session data, and in-memory structures remain intact.

State layout changes require a deployment restart. Hotfix updates **behavior on
existing state** — they cannot add, remove, or modify fields.

## Project Structure

```
Server.App (stable, not hot-reloadable)    Server.Hotfix (hot-reloadable)
────────────────────────────────────       ─────────────────────────────
Data structures                            Business logic
Actor shells (state holders)               RPC service implementations
Bridge entry points                        Message filtering and rules
Program entry point                        Service binding configuration
Service binding configurator

Reference direction: Server.Hotfix → Server.App (one-way)
```

`Server.App` references only framework packages and `Shared`. It does **not**
reference `Server.Hotfix` — the hotfix assembly is loaded dynamically at
runtime via `AssemblyLoadContext`. In development, the `CopyHotfixOutput`
MSBuild target copies `Server.Hotfix.dll` into the App output directory so
`HotfixManager` can discover it via file path.

## Naming Conventions

Lakona uses **Actor model terminology**, not ECS terminology. The ECS terms
Entity, Component, and System are intentionally absent from the public API and
sample code.

| Layer | Convention | Example | Purpose |
|-------|-----------|---------|---------|
| Data | `{Domain}State` | `PlayerState` | Immutable-ish data owned by an actor |
| Actor | `{Domain}Actor` | `ChatRoomActor` | State holder + mailbox, processes messages sequentially |
| Service | `{Domain}Service` | `ChatService` | RPC entry point, orchestrates actor and logic calls |
| Behavior | `{Domain}Behavior` | — | *(Future)* Explicit bridge between actor and hotfix logic |
| Logic | `{Action}` (gerund) | `MessageFiltering` | Pure business logic, stateless where possible |

### Contrast with ECS frameworks

| Concept | ECS (ET / Fantasy) | Actor (Lakona) |
|---------|-------------------|-----------------|
| Concurrency unit | Entity (data bag) | Actor (mailbox + state) |
| Data | Component | State class |
| Logic | System\<T\> | Service / Behavior |
| Naming example | `TransformComponent` + `TransformComponentSystem` | `ChatRoomActor` + `ChatService` |

Actors own their state and process messages sequentially. ECS entities are
passive data bags with systems operating on them from outside. The Actor model
provides stronger encapsulation and is closer to the game-domain concept of
"room", "player", or "session".

## The HotfixDispatch Bridge

### When it is needed

When code in the stable layer (`Server.App`) needs to call logic in the
hot-reloadable layer (`Server.Hotfix`), it can use `HotfixDispatch`:

```csharp
// In Server.App (stable)
return HotfixDispatch.Invoke<ChatState, string, string>(
    "FilterMessage",      // method name
    _state,               // state instance
    text);                // argument
```

`HotfixDispatch` is a static dispatch table rebuilt on every hot reload. It maps
`(stateType, methodName, returnType, parameterTypes)` to a cached delegate. The
call overhead is near-zero after the first invocation.

### When it is NOT needed

When all callers and callees are in the same assembly, direct calls are
preferred:

```csharp
// In Server.Hotfix — ChatService.FilterMessage is a private method
private static string FilterMessage(string text)
{
    var filtered = text.Length > 500 ? text[..500] : text;
    return filtered.Replace("badword", "***", StringComparison.OrdinalIgnoreCase);
}
```

The general rule: **put logic where it is called.** If the only caller is in
Hotfix, the logic belongs in Hotfix. If multiple callers across assemblies need
the same logic, a bridge via `HotfixDispatch` is appropriate.

### Design rationale: Actor vs ECS bridge patterns

ECS frameworks (ET, Fantasy) avoid bridges entirely because their entity data
is passive — systems directly read/write component fields from the same assembly.
The Actor model requires a bridge because:

1. Actor state is encapsulated inside the stable layer.
2. Business logic is in the replaceable layer.
3. The stable layer must never hold direct references to hotfix types (would
   prevent `AssemblyLoadContext` unloading).

`HotfixDispatch` is the minimal abstraction that satisfies these constraints
while preserving type safety on parameters and return values.

## Source-Generated Type-Safe Callers

To reduce boilerplate, the `Lakona.Game.Server.Hotfix.Generators` package
produces extension methods for every `[HotfixState]` type:

```csharp
// Auto-generated in Server.App
public static class ChatStateHotfixCaller
{
    public static TResult Call<TResult>(this ChatState self, string methodName)
        => HotfixDispatch.Invoke<ChatState, TResult>(methodName, self);

    public static TResult Call<TArg, TResult>(this ChatState self, string methodName, TArg arg)
        => HotfixDispatch.Invoke<ChatState, TArg, TResult>(methodName, self, arg);
}
```

Usage:

```csharp
// Before (hand-written)
HotfixDispatch.Invoke<ChatState, string, string>("FilterMessage", _state, text);

// After (generated)
_state.Call<string, string>("FilterMessage", text);
```

The parameter types (`string` argument, `string` return) are checked at compile
time. Only the method name remains a string. Full compile-time safety for method
names is planned for a future release via cross-compilation manifest files.

## Hotfix Lifecycle

```
1. Start              HotfixManager loads Hotfix.dll via AssemblyLoadContext
2. Scan               HotfixSystemScanner finds [HotfixSystemOf] types
3. Build Table        HotfixDispatchTable maps (state, method, types) → delegate
4. Replace            HotfixDispatch.Replace(table) installs new table
5. Unload old ALC     Old AssemblyLoadContext is unloaded, GC collects
```

Services registered via DI are resolved through `ActivatorUtilities` at
construction time. After a hot reload, the DI container still holds references
to the old types — the dispatch table switch is the bridge that routes calls
to the new code.

## Fantasy-Informed Hardening Model

Fantasy's mature server hotfix pattern is useful because it keeps three facts
obvious:

1. Stable data and runtime ownership live outside the hotfix assembly.
2. Hotfix code is loaded through one controlled collectible context.
3. Assembly-level registrations are removed when the collectible context unloads.

Lakona should keep its Actor model boundary, but it must copy those lifecycle
discipline points. Hotfix assemblies must not be loaded by generic feature
discovery, `Assembly.LoadFrom`, or any other default-context path before
`HotfixManager` loads them. Loading a hotfix DLL into the default
`AssemblyLoadContext` makes the assembly non-unloadable for the lifetime of the
process and invalidates the "replaceable logic" model.

The target lifecycle is:

```txt
Resolve version pointer or current source
Read hotfix DLL and optional PDB bytes
Create collectible HotfixAssemblyLoadContext with explicit shared assemblies
Load hotfix assembly from stream
Scan [HotfixSystemOf] methods
Validate every dispatch boundary type resolves from a shared/default context
Create delegates for every method before publishing the table
Atomically replace HotfixDispatch table
Unload the previous collectible context
Confirm the previous context can be collected in tests
```

Stream loading is the default for development and production. It prevents the
running process from holding a file lock on the source DLL and avoids observing
a partially copied DLL after a build. Version-pointer deployment is preferred
for production:

```txt
hotfix/
  current.txt                 -> v2026.06.09.001
  versions/
    v2026.06.09.001/
      Server.Hotfix.dll
      Server.Hotfix.pdb
```

The pointer file must change only after the target directory has been fully
written. File watching should watch the pointer file or a completed version
directory marker, not a DLL being copied in place.

## Shared Assembly Identity

Hotfix methods operate on stable types. Those types must be the same CLR type
instances that the running server already uses. A type with the same full name
loaded into the hotfix `AssemblyLoadContext` is a different type and must be
treated as a reload failure.

The default shared assembly list must include every stable assembly that appears
in hotfix method signatures:

- `Lakona.Game.Server.Hotfix.Abstractions`
- `Shared`
- `Server.App` or the generated app host assembly when hotfix systems target
  app-owned state
- server-only state contract assemblies such as `State.Contracts`

Reload validation must reject any `[HotfixSystemOf]` method whose state,
parameter, or return type is defined in the hotfix assembly load context. That
failure should happen during `ReloadAsync`, before `HotfixDispatch.Replace`, not
on the first gameplay call.

## Feature Discovery Boundary

Feature discovery is a stable-host concern. It may scan the entry assembly and
already-loaded stable assemblies. It must not scan the hotfix directory with
`Assembly.LoadFrom`.

If Lakona later needs hotfix-owned feature registration, it should use an
explicit hotfix manifest loaded by `HotfixManager`, with unregister behavior
attached to the collectible `AssemblyLoadContext.Unloading` event. It should
not let the general feature catalog load hotfix assemblies into the default
context.

## Dispatch Publication Safety

Reload failure keeps the previous dispatch table active. A reload is successful
only after all of these checks pass:

- the resolved DLL exists and can be read completely
- the assembly loads in a collectible context
- scanning finds only supported hotfix method signatures
- duplicate dispatch keys are rejected
- all dispatch boundary types come from shared/default assemblies
- typed delegates for all supported dispatch shapes can be created
- no hotfix assembly was already loaded in the default context by the host

Methods already executing use next-entry semantics: they continue with the
delegate they resolved before reload. New calls see the new table after
publication.

## Explicit Non-Goals

These are not part of the hardening pass:

- multiple independent hotfix domains in one process
- hotfixing actor runtime internals
- hotfixing serializer, transport, or persistence schema layout
- allowing hotfix code to own timers, threads, static subscriptions, or cached
  callbacks
- placing user business systems in `Lakona.Game`

## Comparison with Reference Frameworks

| Aspect | ET | Fantasy | Lakona |
|--------|-----|---------|--------|
| Stable layer | Model (pure data) | Network + Scene container | Server.App (data + actor shells) |
| Hotfix layer | Hotfix (all logic) | Hotfix (logic + config) | Server.Hotfix (logic + services) |
| Concurrency | Entity + System | Entity + System | Actor (mailbox) |
| Cross-layer calls | One-way: Hotfix→Model | One-way: Hotfix→Entity | Bridge: HotfixDispatch |
| Hot reload unit | Assembly | Assembly | Assembly |
| State safety | No state in hotfix | Serialization-based | Stable actor state |

Lakona's design prioritizes the Actor model's encapsulation properties while
adopting AssemblyLoadContext-based hot reload from the broader .NET ecosystem.
The `HotfixDispatch` bridge is a deliberate architectural choice, not a
temporary workaround.

## Design Decisions

### Why not merge Entity (data) and Hotfix (logic) into one project?

ET and Fantasy use a two-project split: data in one assembly, logic in another.
This works for ECS because systems have no internal state beyond what components
hold. In the Actor model, actors own both state AND a processing mailbox —
splitting the actor itself across assemblies would break encapsulation.

Lakona keeps actor shells in `Server.App` (stable) with business logic in
`Server.Hotfix` (replaceable). The boundary is intentional: actor state survives
hot reloads, actor behavior is updated.

### Why not eliminate HotfixDispatch entirely?

Eliminating `HotfixDispatch` would require one of:

1. **Actor logic in Hotfix** — but then actor state (mailbox, members list) would
   be lost on every hot reload unless serialized and restored.
2. **Actor data in Shared** — but Shared is referenced by the Godot client,
   introducing server-only concerns (mailbox, sessions) into client code.
3. **Bidirectional assembly references** — MSBuild prohibits circular project
   references.

`HotfixDispatch` is the minimal mechanism that preserves the Actor model's
guarantees (sequential processing, state ownership, encapsulation) while
enabling hot-reloadable business logic.

### Why `Server.App` instead of `Server.Server`?

The old name `Server.Server` was tautological and didn't communicate purpose.
`Server.App` says "this is the application host" — it wires up dependencies,
owns actor state, and provides the entry point. `Server.Hotfix` says "this is
the replaceable logic." The names make the relationship clear to new users.
