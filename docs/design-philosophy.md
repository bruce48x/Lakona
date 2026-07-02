# Design Philosophy

Lakona is a C# game server framework for teams that want shared contracts,
hot-reloadable server logic, actor-owned mutable state, typed RPC, reliable
push, and explicit cluster routing in one product line.

Lakona's game framework is built on two foundations:

- a process-local actor runtime exposed through `Lakona.Game.Server.Actors`
- `Lakona.Rpc` for transport, serialization, bidirectional RPC, and source
  generation

Everything above those foundations exists to make game-server work simpler:
sessions, reliable delivery, cluster routing, hotfix loading, runtime
guardrails, and generated project scaffolding.

## Runtime Design Principles

Lakona is designed for a small runtime core, explicit service boundaries,
isolated mutable state, fail-fast behavior, and operationally practical hot
updates. These principles are adapted for the .NET and Unity/Godot ecosystem:

- keep the runtime core small and understandable
- make process and network boundaries visible
- isolate mutable game state behind message queues
- treat hot updates as replaceable behavior over stable state
- fail loudly on invalid topology, lost state, and unsafe configuration
- keep framework scope narrow so games own their own business policy

## Core Principles

### Simple Core, Explicit Boundaries

Lakona separates the runtime layers:

```txt
Application game logic
  -> Lakona game infrastructure: sessions, reliable push, cluster, hotfix
  -> Lakona.Rpc: RPC contracts, frames, transport, serializers
  -> actor kernel: mailbox, lifecycle, timers, diagnostics
  -> .NET runtime
```

Lower layers do not know about higher layers. The actor kernel does not know
about networking. RPC does not know about game sessions. Lakona infrastructure
does not contain product-specific gameplay rules.

### Stable State, Replaceable Behavior

Hotfix is not a plugin side path. It is the default authoring model for game
business logic.

Long-lived state stays in stable assemblies: actor fields, session ownership,
RPC transport, persistence handles, timers, diagnostics, and process lifecycle.
Replaceable rules live in `Server.Hotfix` and run against that stable state.

This gives live updates without pretending every part of a running process can
be swapped safely. State shape changes still require a stable deployment and a
compatibility tag change.

### Actors Own Mutable Game State

Rooms, players, lobbies, matchmaking queues, leaderboards, and schedulers are
good actor candidates when they own mutable state and need sequential decisions.

An actor is a concurrency boundary, not an entity framework object. Actor state
is stable. Business behavior belongs in hotfix behavior methods that execute
inside actor turns.

### Distributed Calls Must Show Placement Intent

Remote cost must not disappear behind local-looking magic. Actor calls use
generated selectors:

```csharp
await rooms.Get(roomId).JoinAsync(request, cancellationToken);
await rooms.Local(roomId).JoinAsync(request, cancellationToken);
await rooms.Remote(nodeId, roomId).JoinAsync(request, cancellationToken);
```

`Get(id)` is the normal business path and resolves placement. `Local(id)` means
this process only. `Remote(nodeId, id)` means a specific node. The business
method stays the same, while placement remains visible.

### At-Least-Once With Idempotent Receivers

Exactly-once delivery is the wrong default promise for real-time game servers.
Lakona reliable push uses at-least-once delivery with monotonically increasing
sequence numbers. Receivers detect duplicates and apply each message once at
the business level.

When server state is lost after a crash or restart, the client receives an
explicit lost-state outcome instead of silent data corruption.

### Node Is The Deployment Unit

A node is one OS process. In development, one process can host all features. In
production, features can be split across nodes through configuration. The code
model stays the same; topology is explicit configuration.

### Framework Scope Is Intentionally Narrow

Lakona does not provide:

- account systems or authentication policy
- matchmaking algorithms
- room rules or gameplay simulation
- persistence schema
- reward models, economies, or inventory systems
- client rendering, UI, or physics

Those decisions belong to the game. Lakona provides infrastructure and
guardrails.

## Key Decisions

### Actor Identity

The internal actor kernel uses numeric process-local ids because they are fast,
monotonic, and non-reusable. Public game actor identity uses string-backed
`ActorId` values because game entities need readable, cross-process identifiers
such as `player:alice` or `room:42`.

### Hotfix DLLs

Lakona uses .NET `AssemblyLoadContext` instead of Lua or JavaScript. Hotfix
assemblies can reference stable C# types, use normal debugging tools, and keep
compile-time checks. The tradeoff is intentional: behavior can reload, state
layout cannot casually change under a running process.

### RPC Status

RPC status values describe framework outcomes only. Business failures such as
login rejection, room full, cooldown not ready, or insufficient inventory space
belong in business DTOs.

### Transport And Serializer Choice

Transports and serializers are infrastructure decisions. Gameplay code should
not care whether a contract is carried over TCP, WebSocket, KCP, JSON, or
MemoryPack.
