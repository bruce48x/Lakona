# Contributing To Lakona

This document is for contributors, maintainers, and AI agents working on the
Lakona repository itself. User-facing introductions, quick starts, samples, and
package summaries belong in [README.md](./README.md) and package-specific
`README.md` files.

Lakona is a monorepo for the RPC runtime, process-local actor runtime, and
game-server framework. Treat those parts as one product line with explicit
package boundaries.

Lakona is still in early development. When solving problems, prefer elegant,
thorough fixes over compatibility-preserving patches; breaking compatibility is
acceptable when it leads to a cleaner long-term design.

## Documentation Map

This file is the single authority for contributor workflow and maintenance
rules. Supporting documents describe the active architecture and maintainer
contracts:

| Document | Purpose |
| --- | --- |
| [docs/design-philosophy.md](./docs/design-philosophy.md) | Product principles, skynet lineage, and framework scope boundaries |
| [docs/actor.md](./docs/actor.md) | Actor model, actor kernel boundary, generated selectors, and distributed actor calls |
| [docs/session.md](./docs/session.md) | Session identity, callback binding, disconnect, resume, termination, and Gate / Watchdog / Agent composition |
| [docs/cluster.md](./docs/cluster.md) | Feature, endpoint, RPC service, cluster discovery, routing, and Agar acceptance model |
| [docs/configuration.md](./docs/configuration.md) | Configuration schema, provider precedence, package and environment-variable configuration, Docker Compose shape, feature startup, endpoint rules, JSON array binding, and validation boundary |
| [docs/guardrails.md](./docs/guardrails.md) | Runtime validation model for cluster, hotfix, endpoints, and production profiles |
| [docs/recording.md](./docs/recording.md) | Actor message recording and replay diagnostics model |
| [docs/rpc.md](./docs/rpc.md) | RPC design principles and maintainer reference index |
| [docs/source-generation.md](./docs/source-generation.md) | RPC source-generation policy and generated-code boundary |
| [docs/hotfix/architecture.md](./docs/hotfix/architecture.md) | Hotfix architecture, operational boundary, BuildTag, and deployment model |
| [docs/hotfix/actor-behavior.md](./docs/hotfix/actor-behavior.md) | Mandatory actor state and hotfix behavior authoring rules |
| [docs/hotfix/service-binding.md](./docs/hotfix/service-binding.md) | Generated service binding model for shared RPC contracts and hotfix services |
| [docs/protocol/wire-protocol-v1.md](./docs/protocol/wire-protocol-v1.md) | RPC wire protocol frame contract |
| [docs/protocol/rpc-status-error-model.md](./docs/protocol/rpc-status-error-model.md) | RPC status and error classification model |
| [docs/api-stability/public-api-boundaries.md](./docs/api-stability/public-api-boundaries.md) | RPC public API boundary and compatibility policy |
| [docs/tool/default-experience.md](./docs/tool/default-experience.md) | Generated Lakona project experience and default runtime shape |
| [docs/tool/generation-architecture.md](./docs/tool/generation-architecture.md) | Lakona.Tool generation architecture and regression boundaries |
| [docs/tool/server-pack-command.md](./docs/tool/server-pack-command.md) | Maintainer reference for `lakona-tool server pack` packaging |

Durable design notes belong under `docs/**` when they describe current
behavior or active contributor policy. Delete completed plans, obsolete
roadmaps, and history-only notes instead of keeping them in the default reading
path.

## Quick Workflow

Repository maintenance scripts require PowerShell 7 or newer. Use `pwsh`, not
Windows PowerShell, when running `.ps1` scripts on any platform:

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
```

Use the repository solution for normal validation:

```powershell
dotnet build Lakona.slnx
dotnet test Lakona.slnx --no-build
```

Codex agents should run .NET commands that may restore packages or contact
NuGet with escalated sandbox permissions on the first attempt, instead of first
trying them inside the network-restricted sandbox. This applies to commands such
as `dotnet restore`, `dotnet build` without `--no-restore`, `dotnet test`
without `--no-restore`, `dotnet run` when restore may occur, `dotnet pack` when
restore may occur, and `dotnet tool install` or `dotnet tool update`. After a
successful restore in the same workspace, prefer `--no-restore` or `--no-build`
where appropriate.

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
  *.md                             Active design docs and maintainer references by topic
  hotfix/                          Hotfix architecture and authoring rules
  protocol/                        RPC protocol and status contracts
  api-stability/                   Public API compatibility boundaries
  tool/                            Lakona.Tool design docs

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
- Use `docs/superpowers/**` only for temporary agent plans, reviews, and
  handoff notes while work is active. Before cleanup, move durable rules into
  the relevant current `docs/**` authority document, then delete the completed
  plan or review instead of preserving it as history.
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
