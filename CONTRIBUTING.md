# Contributing To Lakona

This document is for contributors, maintainers, and AI agents working on the
Lakona repository itself. User-facing introductions, quick starts, samples, and
package summaries belong in [README.md](./README.md) and the package-specific
`README.md` files under `src/**`.

Lakona is a monorepo that contains the RPC runtime, process-local actor runtime,
and game-server framework. Treat those parts as one product line with clear
package boundaries, not as three independent codebases.

## Documentation Map

This file is the single authority for contributor and maintenance rules.
Supporting documents provide deeper rationale or user-facing context:

| Document | Purpose |
| --- | --- |
| [README.md](./README.md) | User-facing repository introduction and package map |
| [CHANGELOG.md](./CHANGELOG.md) | Consolidated Lakona release history |
| [docs/game/design-philosophy.md](./docs/game/design-philosophy.md) | Game framework design principles and roadmap |
| [docs/game/lakona-actor-boundary.md](./docs/game/lakona-actor-boundary.md) | Responsibility split between actor runtime and game framework |
| [docs/game/lakona-game-configuration-startup.md](./docs/game/lakona-game-configuration-startup.md) | Game configuration schema, feature catalog startup, and validation boundary |
| [docs/game/lakona-game-runtime-guardrails.md](./docs/game/lakona-game-runtime-guardrails.md) | Runtime validation model for cluster, hotfix, endpoints, and production profile |
| [docs/game/lakona-tool-default-experience.md](./docs/game/lakona-tool-default-experience.md) | Project tool generated experience and default configuration surface |
| [docs/lakona-monorepo.md](./docs/lakona-monorepo.md) | Monorepo structure, naming, and migration policy |
| [docs/rpc/overview.md](./docs/rpc/overview.md) | RPC user-facing overview retained from the former RPC repository |
| [docs/rpc/README.md](./docs/rpc/README.md) | RPC design notes and maintainer-facing decisions |

Add durable design notes under `docs/**` and link them here when they affect
contributor or maintenance policy. Do not put internal architecture RFCs,
migration plans, or contributor-only technical notes under `blog/**`.

`docs/superpowers/**` is a temporary working directory only. Before finishing a
development branch, move any durable decisions or useful design material into
the appropriate permanent documentation under `docs/**`, then delete the entire
`docs/superpowers` directory.

## Quick Workflow

Use the repository solution for normal validation:

```powershell
dotnet build Lakona.slnx
dotnet test Lakona.slnx --no-build
```

For large solution test runs that time out under local tooling, run test
projects sequentially:

```powershell
$projects = Get-ChildItem -Path tests -Recurse -Filter '*.csproj' | Sort-Object FullName
foreach ($project in $projects) {
  dotnet test $project.FullName --no-build
  if ($LASTEXITCODE -ne 0) { throw "Tests failed for $($project.FullName)" }
}
```

Before committing:

- Inspect the staged diff.
- Keep changes scoped to the package, sample, or test area implied by the task.
- Preserve assembly boundaries and package ownership.
- Add or update focused tests for affected runtime contracts.
- Do not commit generated RPC glue, build output, editor caches, local tool
  artifacts, `Library`, `Temp`, `.godot`, `.import`, `bin`, or `obj`.
- If a change modifies shippable package content under `src/**`, apply the
  version bump rules in [NuGet Publishing](#nuget-publishing).

## Repository Layout

```txt
src/
  Lakona.Rpc.Core/                 RPC abstractions, framing, envelopes, protocol limits, serializer contracts
  Lakona.Rpc.Client/               RPC client runtime and generated-client support types
  Lakona.Rpc.Server/               RPC server runtime, host builder, dispatching, and sessions
  Lakona.Rpc.Transport.*           TCP, WebSocket, KCP, and loopback transport implementations
  Lakona.Rpc.Serializer.*          JSON and MemoryPack serializer implementations
  Lakona.Rpc.Analyzers/            RPC analyzer and source generator package
  Lakona.Tool/                     Single CLI tool that owns RPC starter templates and game-framework augmentation

  Lakona.Game.Server/Internal/ActorKernel/  Internal mailbox execution kernel (was standalone Lakona.Actor)
  Lakona.Game.Server.Generators/     Game-facing typed actor spawn and accessor generation

  Lakona.Game.Abstractions/        Cross-side session and reliable-push primitives
  Lakona.Game.Client/              Engine-neutral client helpers and reliable-push tracking
  Lakona.Game.Server/              Server-side hosting, sessions, reliable push, guardrails, health, actor integration
  Lakona.Game.Cluster/             Transport-neutral node, route, messaging, and in-memory cluster primitives
  Lakona.Game.Cluster.Rpc/         RPC-backed cluster messenger and directory adapter
  Lakona.Game.Cluster.Sql/         SQL-backed node directory storage
  Lakona.Game.Server.Hotfix*/      Hotfix runtime, contracts, and generators
  Lakona.Game.Server.Generators/   Game server generator package
  Lakona.Tool/                     Project management and scaffolding tool

tests/
  Lakona.*.Tests/                  Package and sample tests

samples/
  Game.Cluster.TwoNode/            Multi-process cluster smoke sample
  Game.Unity.Agar/                 Unity realtime multiplayer sample
  Game.Godot.Chat/                 Godot .NET single-endpoint chat sample
  Rpc.*                            RPC-focused Unity and Godot samples

docs/
  game/                            Game framework design docs
  rpc/                             RPC maintainer design notes
  lakona-monorepo.md               Monorepo structure, naming, and migration policy

blog/
  game/, rpc/                      Hugo article sources
```

## Product Line Boundaries

Lakona has three layers:

- `Lakona.Rpc`: transport, serialization, RPC calls, protocol primitives, and
  generated bindings.
- `Lakona.Game.Server.Actors`: game-facing actor API backed by an internal mailbox kernel.
  The internal kernel handles mailbox execution, timers, backpressure, and diagnostics.
- `Lakona.Game`: game-session infrastructure that integrates RPC, actor
  execution, reconnect, named endpoint hosting, reliable push, cluster routing,
  hotfix, and runtime guardrails.

User game code owns matchmaking, room rules, gameplay state, account schemas,
inventory, rewards, persistence schema, UI, and product-specific DTOs. Move code
into `src/**` only when it is demonstrably reusable framework infrastructure.

## Actor Runtime Rules

`Lakona.Actor` is a message-driven, process-local service runtime. It is not an
enterprise distributed actor platform.

The core model is:

```txt
actor = mailbox + state + message handler
```

Each actor:

- Owns its state.
- Owns its mailbox.
- Communicates only through messages.
- Processes messages sequentially.
- Receives explicit dependencies through construction, actor refs, or messages.

Because actor state is touched during mailbox turns, state inside a single actor
usually does not need `lock`, `ConcurrentDictionary`, or CAS-style concurrency
protection.

Keep the actor core small and local:

| Boundary | Includes | Excludes |
| --- | --- | --- |
| Messaging | Typed actors, local refs, `Send`, `Call<T>`, timers | Cluster, transparent remoting |
| Mailbox | Bounded capacity, backpressure, stop/drain | Unbounded queues, supervisor trees |
| Lifecycle | Optional start/stop hooks | Persistence, event sourcing, DI activation |
| Registry | Named local actor lookup with type validation | Distributed registry, service discovery |
| Diagnostics | `ActivitySource`, `Meter`, dead letters, slow-message events | APM-specific binding |
| Tooling | Compile-time generators and analyzer diagnostics | Runtime reflection dispatch |
| Application | Process-local actor/mailbox runtime | Network, RPC, serialization, game concepts, Unity |

Generated actor clients are local runtime ergonomics only. They must not become
transparent remote actor proxies or hide cluster routing, serialization, network
latency, timeout, retry, or remote backpressure costs.

Actor context is intentionally narrow. Do not add broad service-locator access
to actor handlers. For long-running work:

1. Capture the input needed for the work.
2. Start the work outside the actor turn.
3. Send a completion message back to the actor.
4. Update actor state when the completion message is processed.

Analyzer warnings should focus on high-confidence blocking patterns inside actor
types, such as `.Wait()`, `.Result`, `Task.WaitAll(...)`, `Task.WaitAny(...)`,
`.GetAwaiter().GetResult()`, and `Thread.Sleep(...)`.

## RPC Architecture Rules

`Lakona.Rpc.Core` defines shared abstractions and protocol primitives. It must
not depend on concrete transports, serializers, client runtime, server runtime,
Unity, or Godot.

Assembly boundary rules:

- `Lakona.Rpc.Client` and `Lakona.Rpc.Server` depend on
  `Lakona.Rpc.Core`, not on concrete transport or serializer packages.
- Transport packages implement `ITransport` and connection acceptors without
  leaking transport-specific assumptions into core RPC code.
- Serializer packages implement `IRpcSerializer` without owning transport,
  session, or dispatch behavior.
- Starter code may reference package names and versions for generation, but
  generated projects must preserve normal runtime package boundaries.
- No circular dependencies between assemblies.

Contracts are the single source of truth:

- Do not duplicate sample contracts into server-local copies.
- Shared contracts should live in the shared/client contract project used by
  both server and client.
- If a server change requires a contract update, edit the shared contract source,
  not a server-local copy.

RPC source generation is the normal glue path. Do not reintroduce starter
scaffolded `Generated/` source folders, Unity editor codegen postprocessors,
MSBuild codegen targets, `codegen.ps1` / `codegen.sh`, CLI tool manifests, or
committed generated RPC glue for new starter projects.

Generated code must be deterministic, IL2CPP-friendly, and avoid heavy
reflection. If Unity source generation fails, fix analyzer compatibility,
packaging, import metadata, or Unity compiler integration instead of falling
back to checked-in generated client source.

## Unity And IL2CPP Rules

Lakona RPC and game samples target Unity 2022 LTS compatibility, including
iOS, IL2CPP, and HybridCLR-sensitive paths. Stability and platform compatibility
take priority over convenience.

Allowed Unity-compatible runtime dependencies:

- `System.Threading.Channels` may be used by runtime packages and Unity samples.
- `System.IO.Pipelines` may appear through transport or serializer dependency
  chains. Validate the affected package path before expanding support claims.

Forbidden in Unity client code, including Unity tests:

- `System.Reflection.Emit`
- Runtime code generation
- APIs relying on JIT-only behavior

Unity client code and shared contracts must compile with C# 9.0. Do not use
C# 10+ syntax in Unity-facing code.

Unity test rules:

- NUnit + Unity Test Framework only.
- EditMode tests live under `Assets/Tests/Editor/**`.
- PlayMode tests live under `Assets/Tests/PlayMode/**` or an asmdef not
  restricted to Editor.
- Do not use `async Task` with `[Test]`. Use `[UnityTest]` plus `IEnumerator`.
- Each Unity test file must alias assertions with
  `using NUnitAssert = NUnit.Framework.Assert;` and use `NUnitAssert.*`.

## Game Framework Boundaries

`Lakona.Game` should not become a full game business framework. Keep the
boundary narrow:

- Framework: connection lifecycle, host integration, session infrastructure,
  reliable push, reusable client state helpers, explicit cluster routing,
  diagnostics, health checks, hotfix infrastructure, and scaffolding.
- Game project: accounts, matchmaking policy, room rules, gameplay simulation,
  UI, persistence schema, and product-specific DTOs.

A concept belongs in `Lakona.Game` only when it is infrastructure, useful across
multiple game genres, compatible with low-latency online workflows, and able to
expose failure, backpressure, and state mismatch explicitly.

Good candidates:

- session identity and endpoint binding
- reliable business push
- named RPC endpoint hosting
- cluster node identity and route location
- route directory and node messenger abstractions
- diagnostics, health checks, and metrics
- optional tool templates for deployment infrastructure

Bad candidates:

- account schemas
- matchmaking rules
- room rules
- battle or skill systems
- AOI implementation
- inventory, guild, leaderboard policy, rewards, quests, or product DTOs
- Unity/Godot UI architecture

Package responsibilities:

- `Lakona.Game.Abstractions` owns framework-owned cross-side primitives such as
  session identity and reliable-push acknowledgement outcomes. User-owned
  business DTOs still belong in the game's own shared project.
- `Lakona.Game.Client` owns engine-neutral client helpers. It must not own Unity
  scene state, UI text, gameplay-specific callbacks, or transport creation
  details unless they can be expressed through small interfaces.
- `Lakona.Game.Server` owns server-side framework infrastructure, not gameplay
  policy.
- `Lakona.Game.Cluster` owns transport-neutral routing contracts and local
  validation implementations.
- `Lakona.Game.Cluster.Rpc` owns the RPC transport adapter for internal cluster
  traffic while keeping core cluster contracts transport-neutral.
- `Lakona.Tool` owns scaffolding and project maintenance commands. Runtime code
  belongs in runtime packages.

## Cluster Architecture

A node is a server process participating in a Lakona cluster. Use `node` when
discussing cluster membership and lifecycle. Gateway, lobby, match, room, chat,
node-directory, route-directory, actor host, client-session host, reliable-push
host, and scheduler host are services that can be composed inside a node, not
fixed node types.

Cluster routing uses two separate responsibilities:

- `IRouteDirectory`: stores `RouteKey -> RouteLocation` with expiration, route
  generation, and node epoch.
- `IClusterRouter`: applies route lookup, TTL checks, local dispatch, remote
  dispatch, and backpressure behavior.

Node communication is a lower layer:

- `INodeMessenger`: sends a `ClusterMessage` to a resolved `RouteLocation`.
- `IClusterMessageHandler`: receives a cluster message on the target node and
  dispatches it locally.

The first cluster model is static-bootstrap plus dynamic-node-directory:

1. Node starts and reads cluster name, node id, configured services, endpoints,
   and labels.
2. Node uses static bootstrap configuration to find a seed or directory endpoint.
3. Node registers with the node directory.
4. Directory assigns or records a node epoch and lease.
5. Node heartbeats until it drains or dies.
6. During draining, node stops accepting new ownership and finishes only bounded
   in-flight work.
7. Expired node leases make the node unavailable for discovery.
8. Expired route leases make affected routes unavailable until another node
   registers a new location.

Do not implement these as default framework behavior in the first cluster module:

- automatic actor migration
- unqualified transparent remote actor calls that hide target selection
- distributed transactions
- exactly-once cross-node delivery
- battle live migration
- fully decentralized route directory
- Raft-based cluster consensus
- cross-node shared mutable objects

## Hotfix Boundary

Hotfix support is useful for game server workflows, but it must not pollute core
actor, RPC, or session APIs.

The authoritative design model is:

```txt
stable runtime state + replaceable business logic
```

Long-lived mutable state should live in stable runtime-owned types or explicit
serialized state. Replaceable business logic can live in a hotfix assembly and
operate on that stable state. Large structural changes, protocol changes, and
persistence schema changes should use deployment or migration workflows.

Hotfix code is behavior, not ownership. Stable actors and runtime hosts own
execution, scheduling, cancellation, I/O, persistence, logging, network push, and
side effects. Hotfix systems must not own long-lived timers, threads, callbacks,
static event subscriptions, cached delegates, or other references that keep an
old `AssemblyLoadContext` alive.

First versions should avoid hotfixing:

- actor runtime internals
- serializer protocol structure
- transport protocol structure
- persistent state schema
- low-level schedulers

## Runtime Safety

Prefer explicit lifetimes:

- `DisposeAsync`
- `StopAsync`
- clear ownership of transports, sessions, background loops, and cancellation

Avoid implicit global state. Favor clarity over micro-optimizations in shared
infrastructure.

Allowed `ValueTask` patterns:

- `return default;` for `ValueTask`
- `return new ValueTask<T>(value);`
- `async` methods returning `ValueTask<T>` with `return value;`

Forbidden:

- `ValueTask.CompletedTask`
- `ValueTask.FromResult(...)`

Transport implementations must be cancellation-safe, avoid background thread
leaks, and make disconnect behavior explicit and testable. Prefer
`LoopbackTransport` for local RPC tests that do not need real sockets.

Diagnostics should use standard .NET APIs:

- `ActivitySource` for traces
- `Meter` for metrics
- events for dead letters, slow messages, timeout diagnostics, and delivery
  failures

Metric tags must stay low-cardinality. Do not put actor ids, actor names,
message payloads, request values, or user-specific identifiers into metric tags.

## Testing Responsibility

Tests should protect runtime contracts rather than mirror implementation details.

| Area | Required coverage when changed |
| --- | --- |
| Actor messaging | Send dispatch, `Call<T>` responses, timeout behavior, response type validation, dead letters |
| Actor mailbox | Send order, single-actor non-concurrency, bounded backpressure, stop drain, metrics |
| Actor lifecycle | Startup hooks, graceful stop hooks, startup failure rollback, disposal behavior |
| Actor tooling | Generated spawn extensions, actor clients, generated source shape, analyzer diagnostics |
| RPC runtime | Envelope encoding, dispatch, session cleanup, connection admission, protocol limits |
| Transports | Cancellation, disconnect, backpressure, framing, transport security |
| Serializers | Roundtrips, payload compatibility, failure behavior |
| Starter/tooling | CLI parsing, dependency planning, generated file layout, template output |
| Game sessions | Resume, cleanup, endpoint binding, token validation, reliable push |
| Cluster | Route lookup, expiration, local dispatch, remote dispatch, stale registration, node restart |
| Hotfix | Dispatch, reload, unload, file watching, generated accessors, failure fallback |
| Unity samples | EditMode or PlayMode tests for Unity-facing runtime behavior and sample shape |

Source-scan tests that read files from `src/**` must be updated when source files
move or are renamed.

## NuGet Publishing

NuGet publishing is handled by GitHub Actions, not by local manual pushes.

Each package version is defined in its `.csproj` through the `<Version>`
property.

Critical rule: any change to shippable library code under `src/**` that should
reach NuGet must bump the affected package version before pushing. Publish
workflows use `--skip-duplicate`; if a changed package keeps an already-published
version, CI can succeed while nuget.org silently skips that package.

Rules:

- Bump the `<Version>` in every modified `src/<PackageName>/<PackageName>.csproj`
  when changing source files in that package for release.
- Bump even for small bug fixes.
- Do not bump versions for docs-only or test-only changes unless they alter
  files packed into a package or otherwise need to ship.
- When bumping a library package consumed by starter/tool scaffolding, update
  the corresponding release-version file, generated template constants, sample
  package references, or changelog entries in the same change.

For local pack verification only:

```powershell
New-Item -ItemType Directory -Force artifacts/nuget | Out-Null
Get-ChildItem src -Filter *.csproj -Recurse | ForEach-Object {
  dotnet pack $_.FullName --no-restore -c Release -o artifacts/nuget
}
```

## Assistant And Maintainer Guardrails

- Follow all rules above.
- Preserve package ownership and assembly boundaries.
- Fix Unity / IL2CPP violations before committing.
- Do not solve source-generation failures by committing generated RPC glue.
- Prefer changes that preserve existing tests and add focused coverage for new
  behavior.
- Keep package README files user-facing.
- Put maintainer rationale in `docs/**` or this file.
- Avoid unrelated refactors unless explicitly requested or necessary to complete
  the task safely.
- Treat `docs/superpowers/**` as temporary. Move useful decisions into durable
  docs, then delete the directory before finishing development work.
