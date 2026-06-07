# Lakona.Game Actor Boundary

`Lakona.Game.Server.Actors` is the only public actor API for game code. The
runtime uses an internal actor kernel under `Lakona.Game.Server.Internal`, but
that kernel is not a package, not a public API, and not something generated
projects should reference directly.

## Responsibility Split

```
Internal ActorKernel                 Lakona.Game.Server.Actors
─────────────────────────────       ─────────────────────────────
Mailbox queue                       Game actor identity (string ActorId)
Sequential dispatch                 Actor base class and context
Call/response slots                 IActorRuntime
Timers                              DI activation
Stop/drain lifecycle                Remote actor calls
Diagnostics mechanism               Cluster routing
Backpressure metrics                Message recording storage
Tell / Call (process-local)         Cluster routing (cross-node)
Timer dispatch                      Reliable push (at-least-once)
Lifecycle (start/stop/drain)        Hotfix (AssemblyLoadContext)
Diagnostics (Activity/Meter)        Gate / Watchdog / Agent patterns
Execution timeout                   Server hosting (DI, config)
Message interceptor hooks           Message recording/replay (storage)
Actor state reporting               Service discovery
```

## Feature Placement Rules

### Belongs in Lakona.Actor

A feature belongs in Lakona.Actor if it answers: **"How does a single actor execute safely?"**

Examples:
- Message dispatch with try-catch isolation
- Mailbox capacity and backpressure
- Timer scheduling and delivery
- Execution timeout (interrupt stuck handlers)
- Call chain tracking and deadlock detection
- Activity/span propagation through Tell/Call
- Message interception hooks (mechanism, not storage)
- ActorId monotonic generation
- Actor lifecycle state (Active → Draining → Dead)

### Belongs in Lakona.Game

A feature belongs in Lakona.Game if it answers: **"How do multiple nodes cooperate?"** or **"How does a game server compose its services?"**

Currently implemented:
- Cluster routing and node directory
- Session resume and token validation
- Reliable push outbox/inbox
- Hotfix assembly loading and dispatch table swap
- Feature Catalog server assembly (`LakonaGameFeature`, `AddLakonaGame`, compact `Lakona.Game:Feature`)
- Remote actor messaging (typed `Local(id)` / `Remote(nodeId, id)` refs over lower-level `AskRemoteAsync` / `TellRemoteAsync` plumbing)
- Message recording storage and replay (`IMessageLogStore`)
- Game-specific ActorId scheme (string with generation)

Potentially belongs here in the future:
- Cross-server event bus (currently: Redis pub-sub recommended)
- Service discovery and leader election (currently: static config + INodeDirectory)

### Belongs in a shared Analyzer

Analyzer rules apply across the boundary:

| Rule | Scope |
|------|-------|
| ULA001 (no self-call) | Lakona.Actor |
| ULA002 (no blocking wait) | Lakona.Actor |
| ULA003 (no discarded call) | Lakona.Actor |
| Actor isolation rules | Shared (future) |
| Thread safety annotations | Shared (future) |

## Configuration Flow

```
Lakona.Game.ActorRuntimeOptions
    └─ maps to → Lakona.Actor.ActorSystemOptions
        ├─ MailboxCapacity
        ├─ SlowMessageThreshold
        ├─ ExecutionTimeout       ← new in 0.3.0
        └─ MessageInterceptor     ← new in 0.3.0
    └─ maps to → Lakona.Actor.ActorSpawnOptions
        └─ MailboxCapacity
```

Lakona.Game adds its own configuration on top:
- `CallTimeout` (for AskAsync)
- Diagnostic event handlers (DeadLetter, SlowMessage, CallTimeout)

## When Lakona.Actor changes, Lakona.Game adapts

| Lakona.Actor change | Lakona.Game adaptation |
|------------------|---------------------|
| New config option | Expose via `ActorRuntimeOptions` |
| New public API | Wrap in `IActorRuntime` if relevant |
| New diagnostic event | Forward through Lakona.Game handler |
| Breaking change | Update facade mapping in `LakonaActorRuntime.cs` |
| New interceptor hook | Implement `IActorMessageInterceptor` for recording |

## Version Compatibility

Lakona.Actor 0.3.0 is a breaking change from 0.2.x:
- `ActorCallTimeoutReason.CircularWait` removed
- `ActorCell` constructor gains `executionTimeout` parameter
- `ActorSystem.Stop` flow reordered (removal after drain)

Lakona.Game must update its `Lakona.Actor` NuGet reference and adapt the facade accordingly.
