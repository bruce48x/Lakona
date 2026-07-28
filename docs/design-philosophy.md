# Design Philosophy

Lakona is a C# game server framework for teams that want shared contracts,
hot-reloadable server logic, actor-owned mutable state, typed RPC, reliable
push, and explicit cluster routing in one product line.

Lakona's game framework is built on two foundations:

- a process-local actor runtime exposed through `Lakona.Game.Server.Actors`
- `Lakona.Rpc` for transport, serialization, bidirectional RPC, and source
  generation

Everything above those foundations exists to make game-server work simpler:
sessions, reliable delivery, cluster routing, standard application HTTP,
hotfix loading, runtime guardrails, and generated project scaffolding.

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

### Complexity Budget

Lakona is early enough that simplifying the long-term model is more important
than preserving compatibility shims. A framework surface should stay only when
it carries active runtime behavior or a clear extension contract.

Maintainers should treat the following as active simplification pressure:

- Remove obsolete public options, aliases, and compatibility fields instead of
  keeping them as passive documentation of old behavior.
- Prefer generated, typed binding over stringly runtime lookup for hotfix
  callbacks, actor calls, and service dispatch surfaces.
- Keep process, DI, serializer, cluster, and hotfix-generation boundaries
  visible. Hidden fallback providers, global service replacement, or ambient
  scopes must remain isolated and documented until they can be replaced by an
  explicit boundary.
- Keep `LakonaGameServer.RunAsync` as the one-command generated-project entry
  point, but do not let it accumulate unrelated startup logic directly. New
  startup responsibilities should be factored behind named composition steps.
- Source generators may generate multiple runtime products, but their internal
  implementation should be organized by product boundary so state accessors,
  RPC service proxies, actor refs, generic actor call helpers, and diagnostics can evolve
  independently.

Current high-priority simplification targets:

- `HotfixGenerator.cs` should keep generated output stable while its internals
  are split by product boundary: state accessors, stable RPC service proxies,
  behavior-derived actor refs and generic call helpers, diagnostics, and shared naming/key
  helpers.
- Hotfix activation should move from implicit root-provider fallback toward an
  explicit stable-dependency bridge so reloadable code can see which stable
  services are intentionally available.
- Timer callbacks and actor calls should move toward typed or behavior-first
  binding instead of user-authored method-name strings where the ergonomics can
  stay good.
- Cluster and remote Actor payload serialization belongs to one
  framework-owned TCP + MemoryPack channel rather than a global
  `IRpcSerializer` replacement or application extension point.
- Notification commands use generated typed helpers; runtime `DispatchProxy`
  capture and reflection-based callback invocation do not belong in the server
  delivery path.
- `LakonaGameServer.RunAsync` should remain the one-line generated-project
  entry point, but internal startup responsibilities should be factored behind
  named composition steps.

`LakonaGameServer.RunAsync` is therefore a thin public facade. The internal
bootstrapper owns pre-provider discovery, service composition, validation, and
host construction; the internal runner owns module startup, initial Hotfix
loading, framework execution, shutdown, and provider disposal. Their order
remains explicit and testable instead of becoming another extensibility
contract.

Complexity review is separate from runtime correctness. Tests can prove that a
capability works without proving that the authoring model is minimal. Generated
starter projects are the strictest user-experience test: if a starter teaches a
concept, that concept should be worth carrying.

## Core Principles

### Simple Core, Explicit Boundaries

Lakona separates the runtime layers:

```txt
Application Hotfix behavior
  -> Lakona game infrastructure: ingress, sessions, reliable push, cluster
     -> ASP.NET Core and Kestrel: management and application HTTP
     -> Lakona.Rpc: bidirectional RPC contracts, frames, and transports
     -> process-local actor runtime: mailbox, lifecycle, call/response
  -> .NET runtime
```

Lower layers do not know about higher layers. The process-local actor mailbox
does not know about networking. RPC does not know about game sessions. Lakona
infrastructure does not contain product-specific gameplay or HTTP rules.

### Stable State, Replaceable Behavior

Hotfix is not a plugin side path. It is the default authoring model for game
business logic.

Long-lived state and shared protocol shape stay in stable assemblies: actor
fields, session ownership, RPC contracts, persistence handles, timers,
diagnostics, and process lifecycle. Application HTTP declarations and handlers
live together in `Server.Hotfix`; the initial generation freezes their
process-local route manifest and stable hosting assigns internal endpoint
slots. Later Hotfix generations may replace behavior but may not mutate that
manifest without a process restart.

Stable external dependencies belong to automatically discovered application
modules in `Server.App`. Modules declare their services before the final root
provider is built, then complete asynchronous initialization before initial
Hotfix loading, listener startup, or cluster Ready publication. Reloadable
behavior therefore never owns database pools, Redis multiplexers, or their
process lifecycle. See [Application Modules](./application-modules.md).

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
await actors.Route<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, cancellationToken);
await actors.Local<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, cancellationToken);
```

`Route(id)` is the normal business path and owns directory lookup plus node
selection. `Local(id)` means this process only after the caller has already
proven local ownership. The behavior method stays explicit, while placement
remains visible.

### At-Least-Once With Idempotent Receivers

Exactly-once delivery is the wrong default promise for real-time game servers.
Lakona reliable push uses at-least-once delivery with monotonically increasing
sequence numbers. Receivers detect duplicates and apply each message once at
the business level.

When server state is lost after a crash or restart, the client receives an
explicit lost-state outcome instead of silent data corruption.

### Node Is The Deployment Unit

A node is one OS process. In development, one process can host all actor kinds. In
production, actor hosts can be split across nodes through configuration. The code
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

The process-local runtime and cluster-facing actor APIs use the same
string-backed `ActorId`. Game entities therefore keep one readable identity
across the local registry, mailbox diagnostics, directory ownership, and remote
routing, such as `player:alice` or `room:42`.

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
MemoryPack. Client-facing endpoints remain explicit application choices;
node-to-node cluster RPC is fixed to TCP + MemoryPack by
`Lakona.Game.Server`.

### Application HTTP

Standard HTTP request/response ingress is a first-class game-server capability,
not an RPC transport. `Lakona.Game.Server` uses one ASP.NET Core host and
Kestrel server for framework Management HTTP and independently configured
Application HTTP listeners.

Payment providers, operations tools, and other HTTP callers do not implicitly
create Game Sessions or gain callback, resume, or reliable-push semantics.
Stable generated binders acquire the current Hotfix generation for each
application request; all product behavior remains in `Server.Hotfix`.
Management routes remain isolated under `/_lakona/**`. See
[Application HTTP](./http.md).
