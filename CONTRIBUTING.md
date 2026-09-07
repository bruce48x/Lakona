# Contributing To Lakona

This file is the single entry point for contributors, maintainers, and AI
agents working on the Lakona repository. Rules are split into focused authority
documents so they remain discoverable without making this entry point bulky.

Lakona is an early-stage monorepo for the RPC runtime, process-local actor
runtime, and game-server framework. Treat them as one product line with explicit
package boundaries. Prefer elegant, thorough fixes over compatibility-preserving
patches when a cleaner long-term design requires a breaking change.

## Required Reading

Use the maps below to select the workflow rules and authority sections relevant
to the current task. Read applicable rules before acting, including additional
required reading they name. Apply all engineering constraints in scope; a small
edit does not waive a relevant rule, but it does not require reading unrelated
architecture or setup instructions.

Reuse material already read and available in the current context, including
reading requested by a skill. Read again only when it has changed or the needed
context is missing; load additional sections when the task scope expands.
The maps are navigation, not a requirement to read every linked document.

| Change scope | Required authority |
| --- | --- |
| Any source, package boundary, generated code, Unity-facing code, or repository maintenance | [Engineering Rules](./docs/contributing/engineering.md) |
| Tests, validation, CI verification, or source-scan coverage | [Testing](./docs/contributing/testing.md) |
| Architecture, complexity, code-smell, or generated-project experience review | [Architecture Review](./docs/contributing/architecture-review.md) |
| Shippable content under `src/**`, package versions, packing, or publishing | [NuGet Publishing](./docs/contributing/nuget-publishing.md) |
| Changelog or release milestone | [Changelog Maintenance](./docs/changelog.md) |

Current architecture and maintainer contracts:

This map is the repository's sole registry of current documentation
authorities. Authority documents do not maintain independent `Status`, `Date`,
`Audience`, or `Last reviewed` headers. Change this map when authority moves or
a document leaves the current contributor path.

| Area | Authority |
| --- | --- |
| Domain language | [Context](./CONTEXT.md) |
| Product principles | [Design Philosophy](./docs/design-philosophy.md) |
| Actors and cluster | [Actors](./docs/actor.md), [Cluster](./docs/cluster.md) |
| Sessions and configuration | [Sessions](./docs/session.md), [Configuration](./docs/configuration.md) |
| Logging integration | [Logging](./docs/logging.md) |
| Metrics, traces, and OpenTelemetry integration | [Observability](./docs/observability.md) |
| Application HTTP | [Application HTTP](./docs/http.md) |
| Application resource lifecycle | [Application Modules](./docs/application-modules.md) |
| Runtime validation | [Guardrails](./docs/guardrails.md) |
| Runtime performance | [Performance](./docs/performance.md), [Cross-Framework Benchmarking](./docs/framework-benchmarking.md) |
| RPC | [Architecture](./docs/rpc/architecture.md), [Source Generation](./docs/rpc/source-generation.md), [Public API Boundaries](./docs/rpc/public-api-boundaries.md), [Wire Protocol](./docs/rpc/wire-protocol-v1.md), [Status Model](./docs/rpc/status-error-model.md) |
| Hotfix | [Architecture](./docs/hotfix/architecture.md), [Actor Behavior](./docs/hotfix/actor-behavior.md), [Service Binding](./docs/hotfix/service-binding.md) |
| Packaging and deployment | [Packaging and Deployment](./docs/deployment.md) |
| Project tooling | [Default Experience](./docs/tool/default-experience.md), [Generation Architecture](./docs/tool/generation-architecture.md), [Lakona Hub](./docs/tool/lakona-hub.md), [Agent Skills](./docs/tool/agent-skills.md), [Package Version Graph](./docs/tool/package-version-graph.md) |

Durable design notes belong under `docs/**`. Delete completed plans, obsolete
roadmaps, and history-only notes instead of retaining them in the default
reading path.

## Repository Setup

Configure the tracked Git hooks once per clone:

```powershell
pwsh -NoProfile -File scripts/git/install-hooks.ps1
```

The pre-commit hook runs the NuGet and Hub release-version guards whenever
staged files can affect published artifacts. This catches missing transitive
consumer bumps before they reach GitHub Actions.

The pre-push hook runs the repository tests with isolated build artifacts, then
runs the default local-package E2E smoke test before contacting the remote. The
E2E packs the local NuGet packages, scaffolds and builds a Godot + WebSocket +
MemoryPack project from that feed, then verifies a real RPC round trip. A failed
test or E2E blocks the push.

## Standard Workflow

Repository scripts require PowerShell 7 or newer. Use `pwsh`, not Windows
PowerShell.

Select edit-time checks according to [Testing](./docs/contributing/testing.md#validation-scope-and-completion).
The commands below are validation entry points, not a sequence to repeat after
every edit. Commit, push, and release gates remain required at their respective
stages, as described here and in the publishing authority.

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
pwsh -NoProfile -File scripts/test.ps1
pwsh -NoProfile -File scripts/check-release-version-guards.ps1
```

The repository test script writes build outputs under `artifacts/test` so an
open Rider Avalonia Designer cannot lock the command-line test build. The
pre-push hook runs this isolated test suite before the local-package E2E smoke
test.

For .NET commands that may restore packages, use the environment's network or
escalated permission mechanism when required by its restrictions. Reuse existing
authorization where it applies; this does not require a separate conversational
approval for every command. After a successful restore, prefer `--no-restore`
or `--no-build` where valid.

Before committing:

- Inspect the staged diff and keep the change scoped to the task.
- Preserve package ownership and assembly boundaries.
- Verify affected runtime contracts using existing coverage; add or update
  focused tests where meaningful coverage is missing, following
  [Testing](./docs/contributing/testing.md#validation-scope-and-completion).
- When implementation changes alter existing architecture, configuration,
  public APIs, generated output, or runtime contracts, update every affected
  authority document in the same change.
- Do not commit generated RPC glue, build output, editor caches, local tool
  artifacts, `Library`, `Temp`, `.godot`, `.import`, `bin`, or `obj`.
- Apply the package-version rules for modified shippable content.
- Run the release-version guards immediately before every commit, even when
  the current edit is test-only, because they validate the accumulated release
  range. The tracked pre-commit hook enforces this for release-input changes.
- Record significant milestones with their date and affected package versions.
- Run the validation required by every authority document in scope.
