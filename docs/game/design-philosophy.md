# Lakona.Game Design Philosophy

## What Lakona.Game Is

Lakona.Game is a **distributed game server framework** built on two core foundations:

- **Lakona.Game.Server internal actor kernel** — process-local mailbox execution, lifecycle, timers, and diagnostics exposed through `Lakona.Game.Server.Actors`
- **Lakona.Rpc** — transport, serialization, and RPC code generation

Lakona.Game adds what games need on top: sessions, reliable message delivery, cluster routing, hot-reloadable business logic, and opinionated patterns for building multiplayer game servers.

## Influences

Lakona.Game's design is informed by skynet:

| Framework | Language | Key strength |
|-----------|----------|-------------|
| [skynet](https://github.com/cloudwu/skynet) | C/Lua | Pragmatic simplicity, fault isolation, decade of production use |

skynet's philosophy of "simple core, explicit boundaries, fail fast" directly
shapes Lakona.Game's architecture.

## Core Principles

### 1. skynet compatibility — the litmus test

Every design decision is evaluated against this question: **"Would skynet's author agree with this?"**

Specifically:

- **Visible target selection over unqualified transparency.** Actor business calls use generated selectors: `Get(id)` for default distributed access, `Local(id)` for current-process access, and `Remote(nodeId, id)` for specified-node access.
- **Fail fast over silent recovery.** Design errors (circular calls, lost state) throw immediately rather than retrying or degrading.
- **Bounded resources over unbounded queues.** Every queue, cache, and timeout has an explicit limit.
- **Independent sandboxes over shared fate.** One actor's failure must not cascade.

### 2. Explicit boundaries between layers

```
Application (game logic, matchmaking, persistence)
    └─ Lakona.Game (sessions, reliable push, cluster, hotfix)
        └─ Lakona.Rpc (transport, serialization, RPC)
        └─ Internal ActorKernel (mailbox, lifecycle, timers)
            └─ .NET (thread pool, TPL Dataflow, System.Threading)
```

Each layer has a well-defined responsibility. Lower layers do not know about higher layers. The internal actor kernel does not know about networking. Lakona.Rpc does not know about game sessions. Lakona.Game does not contain game logic.

### 3. Node is the deployment unit

A node is one OS process. Services (gateway, lobby, room) are composed inside a node through configuration. In development, all services run in one process. In production, they are split across multiple processes — but the code is identical. Only the configuration changes.

### 4. At-least-once with idempotent receivers

The network is unreliable. Rather than attempting perfect exactly-once delivery (impossible in the general case), Lakona.Game provides **at-least-once reliable push** with monotonically increasing sequence numbers. Receivers detect duplicates and apply each message exactly once.

When server state is lost (crash, restart), the client receives an explicit "state lost" signal rather than silently corrupting data. This is a first-class design choice, not an error condition.

### 5. Framework scope is intentionally narrow

Lakona.Game does **not** provide:

- Account systems or authentication
- Matchmaking algorithms
- Game-specific data models
- Persistence schemas
- Client-side rendering or physics

These belong to game projects. The framework provides infrastructure; the game provides content.

## Framework Analysis: What We Absorb and What We Reject

### Absorbed (implemented or planned)

| Feature | Source | Status | Rationale |
|---------|--------|--------|-----------|
| Actor mailbox + diagnostics | skynet | Done (internal ActorKernel) | Core concurrency model |
| Reliable push (at-least-once) | skynet (message log concept) | Done (Lakona.Game) | Business-level delivery guarantee |
| Hot-reloadable business logic | skynet (Lua hotswap) | Done (`Lakona.Game.Server.Hotfix`) | Zero-downtime logic updates |
| Explicit cluster routing | skynet (harbor) | Done (Lakona.Game.Cluster) | Cross-node messaging with visible boundaries |
| Session lifecycle + reconnect | skynet (gate/watchdog/agent) | Done (Lakona.Game.Server) | Connection management |
| Execution timeout | skynet (monitor + signal) | Done (internal ActorKernel) | Stuck actor recovery |
| Message recording hooks | skynet (message log replay) | Done (internal ActorKernel) | Interceptor for recording/replay |
| Actor state machine | skynet (service lifecycle) | Done (internal ActorKernel) | Explicit Active→Draining→Dead |

### Rejected (conflicts with skynet philosophy)

| Feature | Source | Why rejected |
|---------|--------|-------------|
| Unqualified transparent distributed actors | skynet boundary principle | Hides target selection, placement, and failure modes behind local-looking APIs |
| Actor = Entity | skynet service boundary principle | Conflates concurrency unit with data container, leads to overly fine-grained remote calls |
| One-click network calls | skynet explicit-boundary principle | Makes remote cost invisible; violates "remote boundaries are visible" |
| Transparent persistence | skynet narrow-core principle | Persistence is a game-layer concern, not a framework concern |

### Not applicable (different language or domain)

| Feature | Source | Why not applicable |
|---------|--------|--------------------|
| Lua VM per service | skynet | C# uses AssemblyLoadContext for isolation |
| Coroutine pool | skynet | .NET has ValueTask pooling built in |
| Cross-VM proto sharing | skynet | C# type system provides equivalent sharing |

## Design Decisions Log

### Why string-based ActorId in Lakona.Game when the actor kernel uses long?

The internal actor kernel uses `long` for process-local actor identity (fast, monotonic, non-reusable). Lakona.Game uses `string` for game-level identity because game entities need human-readable, cross-process identifiers (e.g., `player:alice`, `room:42`). The string is mapped to a process-local kernel id when interacting with the local runtime.

This mirrors skynet's 32-bit address scheme (8-bit node + 24-bit local) but with more flexibility for game-specific naming.

### Why generated actor selectors instead of transparent routing?

skynet's harbor system keeps cross-node addressing explicit. Lakona.Game follows the same principle with generated actor selectors:

```csharp
await rooms.Get(roomId).JoinAsync(request, cancellationToken);
await rooms.Local(roomId).JoinAsync(request, cancellationToken);
await rooms.Remote(nodeId, roomId).JoinAsync(request, cancellationToken);
```

`Get(id)` is the default business path and resolves local-first through `ActorDirectory` placement. `Local(id)` is current-process only. `Remote(nodeId, id)` is explicitly pinned to a node. The business method names stay the same, failures throw typed actor call exceptions, and business code does not switch over transport result objects or know endpoint names.

The lower-level `AskRemoteAsync` and `TellRemoteAsync` helpers remain plumbing APIs for cluster actor envelopes and reply correlation, not the preferred day-to-day business API.

### Why at-least-once instead of exactly-once?

Exactly-once delivery in a distributed system requires distributed consensus (e.g., two-phase commit), which is too expensive for real-time game messages. At-least-once with idempotent receivers and monotonic sequence numbers provides the same correctness guarantee at a fraction of the cost.

This is the approach used by TCP (sequence numbers + retransmission) and Kafka (offset tracking), adapted for game sessions.

### Why hotfix DLLs instead of Lua or JavaScript?

.NET's `AssemblyLoadContext` provides collectible assembly loading with full access to the C# type system. Hotfix assemblies can reference stable game types directly, with source-generated friend accessors for private state. This preserves type safety and debugging while enabling zero-downtime logic updates.

The tradeoff is that hotfix assemblies cannot modify state layout — only behavior operating on existing state. This is intentional: stable state + replaceable logic is a cleaner separation than "everything is hot-swappable."
