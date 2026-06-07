# Lakona.Game Design Philosophy

## What Lakona.Game Is

Lakona.Game is a **distributed game server framework** built on two core foundations:

- **Lakona.Game.Server internal actor kernel** — process-local mailbox execution, lifecycle, timers, and diagnostics exposed through `Lakona.Game.Server.Actors`
- **Lakona.Rpc** — transport, serialization, and RPC code generation

Lakona.Game adds what games need on top: sessions, reliable message delivery, cluster routing, hot-reloadable business logic, and opinionated patterns for building multiplayer game servers.

## Influences

Lakona.Game's design is informed by four reference frameworks:

| Framework | Language | Key strength |
|-----------|----------|-------------|
| [skynet](https://github.com/cloudwu/skynet) | C/Lua | Pragmatic simplicity, fault isolation, decade of production use |
| [ET](https://github.com/egametang/ET) | C# | Component-based assembly, location-directory patterns, AI-native architecture |
| [Fantasy](https://github.com/qq362946/Fantasy) | C# | Zero-reflection source generation, roaming route system |
| [GeekServer](https://github.com/leeveel/GeekServer) | C# | Compile-time enforcement, TPL Dataflow actor model |

**skynet is the primary influence.** Its philosophy of "simple core, explicit boundaries, fail fast" directly shapes Lakona.Game's architecture. The other three C# frameworks provide inspiration for developer experience and tooling, but their design choices are evaluated against skynet's principles before adoption.

## Core Principles

### 1. skynet compatibility — the litmus test

Every design decision is evaluated against this question: **"Would skynet's author agree with this?"**

If a feature from ET, Fantasy, or GeekServer conflicts with skynet's philosophy, skynet wins. Specifically:

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

This is the "N → 1, 1 → N" pattern observed in ET: develop with everything in one process for easy debugging, deploy with services split for scale.

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
| Component-based assembly (N→1, 1→N) | ET | Planned | Single-process dev, multi-process prod |
| Cross-server event bus | Fantasy (SphereEvent) | Planned | Pub-sub for announcements, leaderboards |
| Managed distributed actor messaging | ET | Done | Typed `Get(id)` / `Local(id)` / `Remote(nodeId, id)` actor refs with typed exception failures |
| Gate auto-routing (Roaming) | Fantasy | Planned | Client-transparent backend routing |
| Service discovery + leader election | ET | Planned | Automatic failover |
| Deadlock detection → immediate failure | GeekServer (adapted) | Done (internal ActorKernel) | Circular calls throw synchronously |
| Execution timeout | skynet (monitor + signal) | Done (internal ActorKernel) | Stuck actor recovery |
| Message recording hooks | skynet (message log replay) | Done (internal ActorKernel) | Interceptor for recording/replay |
| Actor state machine | skynet (service lifecycle) | Done (internal ActorKernel) | Explicit Active→Draining→Dead |

### Rejected (conflicts with skynet philosophy)

| Feature | Source | Why rejected |
|---------|--------|-------------|
| Unqualified transparent distributed actors | ET | Hides target selection, placement, and failure modes behind local-looking APIs |
| Actor = Entity (ECS merged with Actor) | ET, Fantasy | Conflates concurrency unit with data container, leads to overly fine-grained remote calls |
| One-click network calls (network disguised as local method) | Fantasy | Makes remote cost invisible; violates "remote boundaries are visible" |
| Kestrel as network layer | GeekServer | Lakona.Rpc already provides transport abstraction |
| TPL Dataflow as sole actor backend | GeekServer | The internal actor kernel already owns mailbox execution |
| Transparent persistence | GeekServer | Persistence is a game-layer concern, not a framework concern |

### Not applicable (different language or domain)

| Feature | Source | Why not applicable |
|---------|--------|--------------------|
| Lua VM per service | skynet | C# uses AssemblyLoadContext for isolation |
| Coroutine pool | skynet | .NET has ValueTask pooling built in |
| Cross-VM proto sharing | skynet | C# type system provides equivalent sharing |
| Behavior tree / Buff system | ET | Game content, not framework infrastructure |
| Excel config export toolchain | GeekServer | Game tooling, not framework concern |
| AI Skill for framework | Fantasy, ET | Can be added later as CLAUDE.md enhancements |

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

## Roadmap

### Phase 1: Foundation hardening (mostly complete)

- [x] Internal ActorKernel: execution timeout, state machine, interceptor hooks, circular call fast-fail
- [x] Lakona.Game exposes actor kernel features through `ExecutionTimeout`, `GetState()`, and `ActorState`
- [x] Message recording/replay via `IMessageLogStore` and `ActorCell.DispatchAsync` hook

### Phase 2: Developer experience (complete)

- [x] Feature Catalog startup (`LakonaGameFeature`, `AddLakonaGame`, compact `Lakona.Game:Feature`, transport requirements)
- [x] Managed distributed actor messaging (typed `Get(id)` / `Local(id)` / `Remote(nodeId, id)` refs + typed actor call exceptions)
- [x] Gate/Watchdog/Agent pattern documented (see `docs/gate-watchdog-agent.md`)

### Phase 3: Deferred

These are not currently needed. Existing infrastructure or external tools handle them:

- Cross-server event bus — Redis pub-sub is sufficient for most deployments
- Gate auto-routing — actor placement uses `ActorDirectory`; client-transparent backend routing can be added later
- Service discovery with leader election — static config + `INodeDirectory` suffices for most topologies
- Full-link test framework — no pressing need; revisit when concrete requirements emerge
- Soft routing anti-DDoS — can use an external reverse proxy / load balancer

### Phase 4: Future

- [ ] Distributed tracing export (OTLP) — `TraceId` and `Activity` propagation exist; export is plumbing
- [ ] Systematic resource boundary documentation — list all configurable limits in one place
