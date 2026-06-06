# Lakona.Actor Design Philosophy

## Influences

Lakona.Actor is directly inspired by [skynet](https://github.com/cloudwu/skynet), the C/Lua actor framework by Yunfeng that has powered production game servers for over a decade. Many of the design decisions below come from lessons learned in that project.

## Core Principles

### 1. Simple core, complexity at the edges

The actor runtime itself should be small and stable. It provides three primitives: **spawn**, **send**, and **call**. Everything else — scheduling policies, persistence, networking, clustering — belongs in higher layers.

This mirrors skynet's philosophy: "skynet's core hopes to remain simple and stable." The core does not try to be a game engine. It provides concurrency infrastructure and gets out of the way.

### 2. Mailbox serialization is the only concurrency control

Each actor has exactly one mailbox. Messages are processed one at a time, in FIFO order. No locks, no volatile keywords, no `ConcurrentDictionary` inside actor state.

The developer writes single-threaded code. The runtime handles the rest.

This is the fundamental insight shared by Erlang, Akka, skynet, and Lakona.Actor: **serialized message processing eliminates entire categories of concurrency bugs.**

### 3. Remote boundaries are visible

We do not attempt to make cross-process calls look like local calls. Network calls have different failure modes (timeout, partition, backpressure) and different latency profiles (microseconds vs milliseconds). Hiding this difference behind a uniform API leads to systems that work in development and fail in production.

This principle comes directly from skynet's author: "Attempting to erase the difference between network and local communication is wrong. The physical bandwidth gap between bus and TCP should not be forcibly abstracted into one API."

In practice:
- `Send` / `Call` are process-local operations
- Cross-node communication is handled by a higher-level framework (Lakona.Game)
- The naming convention explicitly distinguishes them

### 4. Fail fast on design errors

When the runtime detects a structural problem, it should fail immediately with a clear diagnostic — not paper over it with timeouts, retries, or fallbacks.

- **Circular actor calls** (A → B → A): `InvalidOperationException` thrown synchronously, before any message is queued
- **Mailbox overflow**: `TrySend` returns `MailboxFull` immediately; `Send` blocks only until capacity is available
- **Actor not found**: `InvalidOperationException` thrown immediately

This is in contrast to frameworks that silently queue, retry, or timeout. A circular call chain is a design problem. The runtime should tell you so, loudly and immediately.

### 5. Bounded everything

Every resource has an explicit, configurable limit:

| Resource | Default | Behavior on exhaustion |
|----------|---------|----------------------|
| Mailbox capacity | 1024 | `TrySend` returns `MailboxFull`; `Send` awaits capacity |
| Call queue timeout | per call | `TimeoutException` before the request is accepted |
| Call response timeout | per call | `TimeoutException` after the request is accepted but not answered |
| Slow message threshold | none (opt-in) | Diagnostic event published |

Unbounded queues hide problems until the process runs out of memory. Bounded queues expose bottlenecks early, when they are cheap to fix.

### 6. Observable by default

The runtime emits diagnostics through standard .NET telemetry APIs:

- **ActivitySource** (`Lakona.Actor`): Distributed tracing spans for every message dispatch
- **Meter** (`Lakona.Actor`): Counters for accepted/rejected/processed messages, call timeouts, dead letters; gauges for queue depth
- **Events**: `DeadLetterPublished`, `SlowMessageDetected`, `CallTimedOut`, `ObserverErrorPublished`

No external agent or collector is required. Diagnostics are always on and can be consumed by any OpenTelemetry-compatible backend. Diagnostic events expose message and request type names rather than payload objects, and observer failures are reported separately instead of changing actor execution.

### 7. Compile-time over runtime

Source generators and analyzers catch errors before the process starts:

- `[ActorClient]` generates type-safe proxy classes, eliminating reflection and boxing
- `ULA001`: Detects self-calls (`ctx.Self.Call(...)`) inside an actor
- `ULA002`: Detects blocking waits (`.Wait()`, `.Result`, `Thread.Sleep`) inside an actor
- `ULA003`: Detects discarded call results (swallowed timeout exceptions)

This is the .NET equivalent of skynet's philosophy of catching problems at the earliest possible stage.

### 8. Strongly typed actor APIs

Actor APIs are typed-only. Actors implement `IActor<TMessage>`, callers hold message-only `ActorRef<TMessage>`, timers accept `TMessage`, and named actor lookup requires the expected message type. Spawn returns `ActorHandle<TMessage>` for lifecycle and diagnostics so communication references do not imply management authority; passing a message-only reference requires explicit `.Ref`. `ActorContext<TMessage>` exposes only current-turn capabilities such as `Self`, response, and timers; it does not expose the full `ActorSystem`. There is no untyped `object` dispatch path and no runtime `dynamic` fallback.

This eliminates a class of errors — wrong message types, missing handlers, accidental type coercion — at compile time rather than at runtime.

## Boundary Rationale

Lakona.Actor deliberately excludes features that other actor frameworks treat as core. Each exclusion is a design choice, not an omission.

### Why not distributed?

Lakona.Actor is a process-local runtime. Distributed features — cluster routing, virtual actors, service discovery, transparent remote references — require different failure models (partition, timeout, retry) and different cost profiles (serialization, network latency). Hiding these behind a uniform local/remote actor API produces systems that work in development and fail under production network conditions.

Related concerns like RPC, serialization, and transport belong in [Lakona.Rpc](https://github.com/bruce48x/lakona).

### Why not persistence?

Actor state is in-memory. Persistence, event sourcing, and snapshotting introduce their own design space — storage backends, serialization formats, consistency models, replay semantics. These are application-level concerns that should not be coupled to the mailbox runtime.

### Why not supervision?

Supervisor trees and hierarchical failure handling add complexity that most game server architectures handle at a higher level (process monitors, health checks, orchestrators). Lakona.Actor provides lifecycle hooks for local cleanup but delegates fault recovery to the host process.

### Why not game concepts?

Rooms, matches, sessions, players, AOI, and other game-specific abstractions have their own lifecycle, networking, and persistence needs. Coupling them to the actor runtime would force a single game architecture on all consumers. These belong in [Lakona.Game](https://github.com/bruce48x/lakona) or application code.

### Decision rule

If a feature needs serialization, network routing, persistence, distributed identity, or gameplay semantics, it belongs outside Lakona.Actor.

## What Lakona.Actor Is NOT

- **Not distributed.** It is a process-local actor runtime. Clustering, service discovery, and cross-node messaging belong to higher-level frameworks.
- **Not a game engine.** It has no concept of players, rooms, sessions, or game logic.
- **Not a persistence layer.** Actors are in-memory. State serialization and storage are the application's responsibility.
- **Not transparent.** Cross-process communication requires different APIs with different failure modes.

## Design Tradeoffs

### Single-threaded per actor vs work-stealing

Lakona.Actor uses `ActionBlock<T>` with `MaxDegreeOfParallelism = 1` per actor. This guarantees sequential execution but means a single slow actor can occupy a thread pool thread. The runtime deliberately does not preempt or skip a running actor turn: timing out the wait would let the mailbox advance while the original handler may still be touching actor state. Slow-message diagnostics make this visible, and recovery belongs to application shutdown or process supervision.

### ActionBlock vs custom scheduler

We use TPL Dataflow's `ActionBlock` rather than a custom work queue. This trades some control (no custom scheduling policies) for robustness (battle-tested by the .NET team) and simplicity (no custom thread management).

### ValueTask vs Task

Actor message handlers return `ValueTask` to avoid allocations for synchronous completions. The runtime awaits the handler directly and preserves the actor turn boundary; it does not wrap handlers in preemptive timeout machinery.

## Versioning

Lakona.Actor follows semantic versioning. While the runtime is still pre-1.0, breaking changes may occur between 0.x versions as the public API is hardened. After 1.0, breaking changes require a major version increment.
