# Contributing To Lakona

This file is the single entry point for contributors, maintainers, and AI
agents working on the Lakona repository. Rules are split into focused authority
documents so they remain discoverable without making this entry point bulky.

Lakona is an early-stage monorepo for the RPC runtime, process-local actor
runtime, and game-server framework. Treat them as one product line with explicit
package boundaries. Prefer elegant, thorough fixes over compatibility-preserving
patches when a cleaner long-term design requires a breaking change.

## Required Reading

Before changing anything, every contributor and AI agent must:

1. Read this file completely.
2. Read every authority document below whose scope the change touches.
3. Follow links from those documents when they declare additional required
   reading.

| Change scope | Required authority |
| --- | --- |
| Any source, package boundary, generated code, Unity-facing code, or repository maintenance | [Engineering Rules](./docs/contributing/engineering.md) |
| Tests, validation, CI verification, or source-scan coverage | [Testing](./docs/contributing/testing.md) |
| Shippable content under `src/**`, package versions, packing, or publishing | [NuGet Publishing](./docs/contributing/nuget-publishing.md) |
| Changelog or release milestone | [Changelog Maintenance](./docs/changelog.md) |

Current architecture and maintainer contracts:

| Area | Authority |
| --- | --- |
| Domain language | [Context](./CONTEXT.md) |
| Product principles | [Design Philosophy](./docs/design-philosophy.md) |
| Actors and cluster | [Actors](./docs/actor.md), [Cluster](./docs/cluster.md) |
| Sessions and configuration | [Sessions](./docs/session.md), [Configuration](./docs/configuration.md) |
| Runtime validation and recording | [Guardrails](./docs/guardrails.md), [Recording](./docs/recording.md) |
| RPC and generation | [RPC](./docs/rpc.md), [Source Generation](./docs/source-generation.md) |
| Hotfix | [Architecture](./docs/hotfix/architecture.md), [Actor Behavior](./docs/hotfix/actor-behavior.md), [Service Binding](./docs/hotfix/service-binding.md) |
| Protocol and API stability | [Wire Protocol](./docs/protocol/wire-protocol-v1.md), [Status Model](./docs/protocol/rpc-status-error-model.md), [Public API Boundaries](./docs/api-stability/public-api-boundaries.md) |
| Project tooling | [Default Experience](./docs/tool/default-experience.md), [Generation Architecture](./docs/tool/generation-architecture.md), [Lakona Hub](./docs/tool/lakona-hub.md), [Package Version Graph](./docs/tool/package-version-graph.md), [Server Pack](./docs/tool/server-pack-command.md) |

Durable design notes belong under `docs/**`. Delete completed plans, obsolete
roadmaps, and history-only notes instead of retaining them in the default
reading path.

## Standard Workflow

Repository scripts require PowerShell 7 or newer. Use `pwsh`, not Windows
PowerShell.

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
dotnet build Lakona.slnx
dotnet test Lakona.slnx --no-build
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

AI agents in network-restricted sandboxes must request the environment's
network or escalated permission before .NET commands that may restore packages.
After a successful restore, prefer `--no-restore` or `--no-build` where valid.

Before committing:

- Inspect the staged diff and keep the change scoped to the task.
- Preserve package ownership and assembly boundaries.
- Add or update focused tests for affected runtime contracts.
- Do not commit generated RPC glue, build output, editor caches, local tool
  artifacts, `Library`, `Temp`, `.godot`, `.import`, `bin`, or `obj`.
- Apply the package-version rules for modified shippable content.
- Record significant milestones with their date and affected package versions.
- Run the validation required by every authority document in scope.
