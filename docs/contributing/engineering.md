# Engineering Rules

This document is required reading for changes to source, package boundaries,
generated code, Unity-facing code, or repository maintenance.

## Repository Layout

```txt
src/         Runtime, serializers, transports, analyzers, game framework, and tooling
tests/       Package and sample tests
samples/     Game-framework and RPC-focused samples
benchmarks/  Framework-neutral benchmark tooling and adapter applications
docs/        Current design and maintainer authorities
blog/        Hugo article sources
```

## Package Boundaries

- `Lakona.Rpc.Core` defines shared RPC abstractions and protocol primitives. It
  must not depend on concrete transports, serializers, client runtime, server
  runtime, Unity, or Godot. Its project must not name consumer or implementation
  assemblies through `InternalsVisibleTo`; cross-package cooperation belongs
  behind explicit interfaces owned by Core.
- `Lakona.Rpc.Client` and `Lakona.Rpc.Server` depend on `Lakona.Rpc.Core`, not
  on concrete transport or serializer packages.
- Transport packages own transport behavior without leaking transport-specific
  assumptions into core RPC code. Serializer packages do not own transport,
  session, or dispatch behavior.
- `Lakona.Game.Server.Actors` owns the process-local actor runtime and uses an
  internal mailbox implementation for bounded sequential dispatch; it is not
  a distributed actor platform. Its Actor Directory interface is a narrow
  port consumed by process-local hosting. Distributed Actor Directory layout,
  transfer and recovery, plus Startup affinity and lifecycle RPC adapters,
  belong under `Cluster/Actors`.
- `Lakona.Game.Server` owns cluster contracts, the Membership state machine,
  routing, messaging, diagnostics, and the fixed TCP + MemoryPack cluster RPC
  implementation. Keep `Lakona.Game.Cluster` as a domain namespace inside that
  package; do not extract a parallel cluster runtime assembly. External
  Membership storage belongs in explicit `Lakona.Game.Clustering.*` Adapter
  packages which depend on the Server-owned `IMembershipTable` interface. The
  core Server package must not depend on database or cache client libraries.
- `Lakona.Game.Server` owns the Hotfix authoring and compiler interface in the
  `Lakona.Game.Server.Hotfix.Abstractions` namespace. App and Hotfix are one
  application split only for replacement and loading: App references
  `Lakona.Game.Server`, Hotfix references App, and the collectible load context
  shares the framework assembly. Do not reintroduce a separate Hotfix
  abstractions project, assembly, or package. The matching compiler extension
  remains an internal asset carried by `Lakona.Game.Server`.
- `Lakona.Game` owns reusable session, host, reliable-push, cluster-routing,
  diagnostics, health, hotfix, and scaffolding infrastructure.
- Game projects own accounts, matchmaking policy, room rules, gameplay,
  persistence schema, UI, and product-specific DTOs.
- `Lakona.ProjectSystem` owns reusable project inspection and project creation,
  including defaults, validation, planning, rendering, transactional writes,
  and Git initialization. Project maintenance behavior belongs behind the same
  boundary.
  `Lakona.Tool` and `Lakona.Hub` are user-facing adapters over that tooling
  seam and must not implement parallel project generators. Runtime code belongs
  in runtime packages.
- Shared contracts are authoritative. Do not duplicate them into server-local
  copies.

## Contributor Guardrails

- Follow `CONTRIBUTING.md` and every authority document selected by its reading
  table. Avoid unrelated refactors unless required for a safe solution.
- Keep package README files user-facing; put maintainer rationale in current
  `docs/**` authorities, not blog posts or completed implementation plans.
- Use `docs/plans/**` only for temporary active plans, reviews, and
  handoffs. Move durable rules to an authority document, then delete completed
  artifacts.
- Do not preserve removed branding, old package names, or migration history
  without an active compatibility reason.
- Do not reintroduce scaffolded `Generated/` source folders, Unity editor
  codegen postprocessors, MSBuild codegen targets, CLI tool manifests, or
  committed generated RPC glue for new projects.
- Generated code must be deterministic, IL2CPP-friendly, and avoid heavy
  reflection.
- Preserve Unity 2022 LTS and C# 9.0 compatibility for Unity-facing runtime,
  samples, and shared contracts. Do not use `Reflection.Emit`, runtime code
  generation, or JIT-only behavior there.
- Prefer explicit lifetimes with cancellation-safe loops and clear transport
  and session ownership.
- Use `ActivitySource`, `Meter`, and events for standard diagnostics. Keep
  metric tags low-cardinality; never tag actor IDs, payloads, request values,
  or user identifiers.
- Allowed `ValueTask` patterns are `return default;`,
  `return new ValueTask<T>(value);`, and `async ValueTask<T>` methods returning
  a value. Do not use `ValueTask.CompletedTask` or `ValueTask.FromResult(...)`.
