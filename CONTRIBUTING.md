# Contributing To Lakona

This document is for contributors, maintainers, and AI agents working on the
Lakona repository itself. User-facing introductions, quick starts, samples, and
package summaries belong in [README.md](./README.md) and package-specific
`README.md` files.

Lakona is a monorepo for the RPC runtime, process-local actor runtime, and
game-server framework. Treat those parts as one product line with explicit
package boundaries.

## Documentation Map

This file is the single authority for contributor workflow and maintenance
rules. Supporting documents hold current architecture details:

| Document | Purpose |
| --- | --- |
| [docs/game/design-philosophy.md](./docs/game/design-philosophy.md) | Current game framework principles and scope boundaries |
| [docs/game/hotfix-architecture.md](./docs/game/hotfix-architecture.md) | Current hotfix architecture and operational boundary |
| [docs/game/session-lifecycle.md](./docs/game/session-lifecycle.md) | Current game session identity, binding, disconnect, resume, and termination model |
| [docs/game/generated-hotfix-service-binding.md](./docs/game/generated-hotfix-service-binding.md) | Current generated service binding model for shared RPC contracts and hotfix services |
| [docs/game/actor-kernel-boundary.md](./docs/game/actor-kernel-boundary.md) | Actor kernel and game framework responsibility split |
| [docs/game/distributed-feature-cluster-model.md](./docs/game/distributed-feature-cluster-model.md) | Current distributed Feature, endpoint, RPC service, cluster discovery, and Agar acceptance model |
| [docs/game/lakona-game-configuration-startup.md](./docs/game/lakona-game-configuration-startup.md) | Game configuration schema and startup validation boundary |
| [docs/game/lakona-game-runtime-guardrails.md](./docs/game/lakona-game-runtime-guardrails.md) | Runtime validation model for cluster, hotfix, endpoints, and production profile |
| [docs/tool/lakona-tool-generation-architecture.md](./docs/tool/lakona-tool-generation-architecture.md) | Current Lakona.Tool generation architecture and regression boundaries |

Durable design notes belong under `docs/**` when they describe current
behavior or active contributor policy. Delete completed plans, obsolete
roadmaps, and history-only notes instead of keeping them in the default reading
path.

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
  Lakona.Rpc.Core/                 RPC abstractions, protocol primitives, serializer contracts
  Lakona.Rpc.Client/               RPC client runtime and generated-client support types
  Lakona.Rpc.Server/               RPC server runtime, host builder, dispatching, and sessions
  Lakona.Rpc.Transport.*           TCP, WebSocket, KCP, and loopback transports
  Lakona.Rpc.Serializer.*          JSON and MemoryPack serializers
  Lakona.Rpc.Analyzers/            RPC analyzer and source generator package
  Lakona.Game.Abstractions/        Shared game framework primitives
  Lakona.Game.Client/              Engine-neutral client helpers
  Lakona.Game.Server/              Server hosting, sessions, reliable push, actors, guardrails
  Lakona.Game.Cluster*/            Cluster routing, messaging, and directory adapters
  Lakona.Game.Server.Hotfix*/      Hotfix runtime, contracts, and generators
  Lakona.Tool/                     Project scaffolding and maintenance commands

tests/
  Lakona.*.Tests/                  Package and sample tests

samples/
  Game.*                           Game framework samples
  Rpc.*                            RPC-focused samples

docs/
  game/                            Current game framework design docs
  rpc/                             Current RPC maintainer docs
  tool/                            Current Lakona.Tool design docs

blog/
  game/, rpc/                      Hugo article sources
```

## Package Boundaries

- `Lakona.Rpc.Core` defines shared RPC abstractions and protocol primitives. It
  must not depend on concrete transports, serializers, client runtime, server
  runtime, Unity, or Godot.
- `Lakona.Rpc.Client` and `Lakona.Rpc.Server` depend on `Lakona.Rpc.Core`, not
  on concrete transport or serializer packages.
- Transport packages implement transport and acceptor behavior without leaking
  transport-specific assumptions into core RPC code.
- Serializer packages implement serializer behavior without owning transport,
  session, or dispatch behavior.
- `Lakona.Game.Server.Actors` is a game-facing actor API backed by an internal
  process-local mailbox kernel. It is not a distributed actor platform.
- `Lakona.Game` owns reusable game-session infrastructure: connection
  lifecycle, host integration, reliable push, explicit cluster routing,
  diagnostics, health checks, hotfix infrastructure, and scaffolding.
- Game projects own accounts, matchmaking policy, room rules, gameplay
  simulation, UI, persistence schema, and product-specific DTOs.
- `Lakona.Tool` owns project generation and maintenance commands. Runtime code
  belongs in runtime packages.

Shared contracts are the source of truth. Do not duplicate sample contracts into
server-local copies. If a server change requires a contract update, edit the
shared contract source used by both server and client.

## Contributor Guardrails

- Follow all rules in this file and the current docs linked from the
  documentation map.
- Avoid unrelated refactors unless they are necessary to complete the task
  safely.
- Keep package README files user-facing.
- Put maintainer rationale in current `docs/**` files, not in blog posts or
  completed implementation plans.
- Do not preserve removed framework branding, old package names, or migration
  history in current documentation unless there is an active compatibility
  reason.
- Do not reintroduce starter scaffolded `Generated/` source folders, Unity
  editor codegen postprocessors, MSBuild codegen targets, CLI tool manifests,
  or committed generated RPC glue for new starter projects.
- Generated code must be deterministic, IL2CPP-friendly, and avoid heavy
  reflection.
- Preserve Unity 2022 LTS compatibility for Unity-facing runtime and sample
  code. Unity client code and shared contracts must compile with C# 9.0.
- Do not use `System.Reflection.Emit`, runtime code generation, or JIT-only
  behavior in Unity client code or Unity tests.
- Prefer explicit lifetimes: `DisposeAsync`, `StopAsync`, clear transport and
  session ownership, and cancellation-safe background loops.
- Use standard .NET diagnostics: `ActivitySource` for traces, `Meter` for
  metrics, and events for dead letters, slow messages, timeout diagnostics, and
  delivery failures.
- Keep metric tags low-cardinality. Do not put actor ids, actor names, message
  payloads, request values, or user-specific identifiers into metric tags.
- Allowed `ValueTask` patterns are `return default;`, `return new ValueTask<T>(value);`,
  and `async` `ValueTask<T>` methods with `return value;`. Do not use:
  - `ValueTask.CompletedTask`
  - `ValueTask.FromResult(...)`

## Testing Responsibility

Tests should protect runtime contracts rather than mirror implementation
details.

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
| Game sessions | Resume, cleanup, session callback binding, token validation, reliable push |
| Cluster | Route lookup, expiration, local dispatch, remote dispatch, stale registration, node restart |
| Hotfix | Dispatch, reload, unload, file watching, generated accessors, failure fallback |
| Unity samples | EditMode or PlayMode tests for Unity-facing runtime behavior and sample shape |

Unity tests use NUnit plus Unity Test Framework. Use `[UnityTest]` plus
`IEnumerator` for async Unity tests, and alias assertions with
`using NUnitAssert = NUnit.Framework.Assert;`.

Source-scan tests that read files from `src/**` must be updated when source
files move or are renamed.

## NuGet Publishing

NuGet publishing is handled by GitHub Actions, not by local manual pushes.

Each package version is defined in its `.csproj` through the `<Version>`
property.

Critical rule: any change to shippable library code under `src/**` that should
reach NuGet must bump the affected package version before pushing. Publish
workflows use `--skip-duplicate`; if a changed package keeps an
already-published version, CI can succeed while nuget.org silently skips that
package.

Rules:

- Bump the `<Version>` in every modified `src/<PackageName>/<PackageName>.csproj`
  when changing source files in that package for release.
- Bump even for small bug fixes.
- Do not bump versions for docs-only or test-only changes unless they alter
  files packed into a package or otherwise need to ship.
- When bumping a library package consumed by generated project scaffolding,
  update the corresponding release-version file, generated template constants,
  sample package references, or changelog entries in the same change.

For local pack verification only:

```powershell
New-Item -ItemType Directory -Force artifacts/nuget | Out-Null
Get-ChildItem src -Filter *.csproj -Recurse | ForEach-Object {
  dotnet pack $_.FullName --no-restore -c Release -o artifacts/nuget
}
```
