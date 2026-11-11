# Standards Conformance Checklist

Rule inventory for Pass 8 of `lakona-architecture-review`. This file is an
**index of checkable rules, not a restatement of them**: read each cited
section before checking it. If a cited document renames a section or changes a
rule, update the pointer here in the same change — a stale pointer must fail
loudly, never drift silently.

IDs identify checklist entries, not source bullet positions. Locate rules by
the linked section and named topic below; the source wording remains authoritative.

Each family is one top-level area in the Pass 8 coverage ledger. For every
family record: scanned count, finding count, and the `Not applicable`
justification when repository evidence proves the delta cannot affect it.
Every finding must quote the offending hunk and cite the rule file and
section.

## CONTRIBUTING.md

Source: [CONTRIBUTING.md — Standard Workflow](../../../../CONTRIBUTING.md#standard-workflow),
including its `Before committing` checklist.

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| CT-01 | Does the delta keep each change scoped to its task, with an inspected staged diff and no unrelated refactors? | Standard Workflow — staged diff and task scope |
| CT-02 | Does the delta update every affected authority document in the same change when it alters architecture, configuration, public APIs, generated output, or runtime contracts? | Standard Workflow — affected authority documents |
| CT-03 | Does the delta avoid committing generated RPC glue, build output, editor caches, `Library`, `Temp`, `.godot`, `.import`, `bin`, or `obj`? | Standard Workflow — excluded generated and build artifacts |
| CT-04 | Does the delta apply package-version rules to modified shippable content, and did the release-version guards pass over the accumulated release range? | Standard Workflow — package versions and release-version guards |

## Package Boundaries

Source: [Engineering Rules — Package Boundaries](../../../../docs/contributing/engineering.md#package-boundaries).

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| EN-01 | Does `Lakona.Rpc.Core` stay free of concrete transport/serializer/client/server/Unity/Godot dependencies, with no consumer-naming `InternalsVisibleTo`? | Package Boundaries — Core dependencies and explicit interfaces |
| EN-02 | Do `Lakona.Rpc.Client`/`Lakona.Rpc.Server` depend on `Lakona.Rpc.Core` without depending on concrete transport or serializer packages? | Package Boundaries — Client and Server dependencies |
| EN-03 | Do transport packages own transport behavior without leaking transport-specific assumptions into Core, and do serializers avoid owning transport, session, or dispatch behavior? | Package Boundaries — transport and serializer ownership |
| EN-04 | Does `Lakona.Game.Server.Actors` keep the process-local runtime and narrow Directory port, with distributed Directory and Startup adapters under `Cluster/Actors`? | Package Boundaries — process-local Actors and distributed adapters |
| EN-05 | Does Server own the cluster runtime and fixed TCP + MemoryPack RPC, with external Membership storage in explicit `Lakona.Game.Clustering.*` adapters and no database/cache client dependency in core Server? | Package Boundaries — cluster namespace, Membership adapters, and Server dependencies |
| EN-06 | Does the Hotfix boundary hold — App references `Lakona.Game.Server`, Hotfix references App, the collectible load context shares the framework assembly, and no separate Hotfix abstractions package is introduced? | Package Boundaries — Hotfix authoring, timers, and compiler ownership |
| EN-07 | Does `Lakona.Game` own reusable infrastructure while game projects own accounts, matchmaking policy, room rules, gameplay, persistence schema, UI, and product DTOs? | Package Boundaries — framework and game-project ownership |
| EN-08 | Does `Lakona.ProjectSystem` own project inspection/creation and maintenance, with `Lakona.Tool` and `Lakona.Hub` staying user-facing adapters and no parallel project generators? | Package Boundaries — ProjectSystem and tooling adapters |
| EN-09 | Are shared contracts kept authoritative, with no server-local duplicate copies? | Package Boundaries — shared contracts |

## Contributor Guardrails

Source: [Engineering Rules — Contributor Guardrails](../../../../docs/contributing/engineering.md#contributor-guardrails).

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| EN-10 | Does the delta follow the selected authorities and avoid unrelated refactors unless required for a safe solution? | Contributor Guardrails — required authorities and task scope |
| EN-11 | Are package README files user-facing, with maintainer rationale in current `docs/**` authorities rather than blog posts or completed implementation plans? | Contributor Guardrails — README audience and maintainer rationale |
| EN-12 | Is `docs/plans/**` used only for temporary active plans, reviews, and handoffs — durable rules moved to an authority document and completed artifacts deleted? | Contributor Guardrails — temporary plans |
| EN-13 | Does the delta avoid reintroducing removed branding, old package names, or migration history without an active compatibility reason? | Contributor Guardrails — removed branding and compatibility history |
| EN-14 | Does the delta avoid reintroducing scaffolded `Generated/` folders, Unity editor codegen postprocessors, MSBuild codegen targets, CLI tool manifests, or committed generated RPC glue for new projects? | Contributor Guardrails — generated-project exclusions |
| EN-15 | Is generated code deterministic and IL2CPP-friendly without heavy reflection? | Contributor Guardrails — generated-code properties |
| EN-16 | Does Unity-facing runtime, samples, and shared contracts stay Unity 2022 LTS / C# 9.0 compatible — no `Reflection.Emit`, runtime codegen, or JIT-only behavior? | Contributor Guardrails — Unity compatibility |
| EN-17 | Are lifetimes explicit with cancellation-safe loops and clear transport and session ownership? | Contributor Guardrails — lifetime and ownership preferences |
| EN-18 | Do diagnostics use `ActivitySource`, `Meter`, and events, with metric tags low-cardinality and free of actor IDs, payloads, request values, and user identifiers? | Contributor Guardrails — standard diagnostics and metric tags |
| EN-19 | Do all `ValueTask` uses follow the allowed patterns — `return default;`, `new ValueTask<T>(value)`, or `async ValueTask<T>` methods returning a value — with no `ValueTask.CompletedTask` or `ValueTask.FromResult(...)`? | Contributor Guardrails — allowed ValueTask patterns |
| EN-20 | Are package READMEs concise entry points that link to detailed guides, with absolute repository URLs for links in NuGet-shipped READMEs? | Contributor Guardrails — concise README content and shipped links |
| EN-21 | Are sample READMEs concise, with design notes limited to distinct boundaries, decisions, and limitations rather than duplicated framework contracts or implementation inventories? | Contributor Guardrails — sample documentation |

## Tests

Source: [Testing](../../../../docs/contributing/testing.md), especially
[Validation Scope And Completion](../../../../docs/contributing/testing.md#validation-scope-and-completion),
[Coverage By Contract](../../../../docs/contributing/testing.md#coverage-by-contract),
[Scheduled and sample validation](../../../../docs/contributing/testing.md#scheduled-and-sample-validation),
and [Repository Test Entry Point](../../../../docs/contributing/testing.md#repository-test-entry-point).

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| TE-01 | Do the delta's tests protect runtime contracts rather than mirror implementation details? | Testing — runtime-contract principle |
| TE-02 | Are the affected contracts covered by relevant existing tests, with tests added or updated only where meaningful coverage is missing? | Validation Scope And Completion; Coverage By Contract |
| TE-03 | Do Unity tests use NUnit + Unity Test Framework, `[UnityTest]` with `IEnumerator` for async, and `NUnitAssert` aliasing? | Scheduled and sample validation — Unity test conventions |
| TE-04 | When the delta moves or renames `src/**` files, are the source-scan tests updated in the same change? | Scheduled and sample validation — source-scan maintenance |
| TE-05 | When repository-wide validation is required, does it use `scripts/test.ps1` with its isolation and checks, monitoring long runs without restarting the suite? | Repository Test Entry Point — unified runner and long runs |
| TE-06 | Is verification proportional to the change, with required stage gates completed and any broader or repeated checks justified by new changes, failures, or concrete unresolved risk? | Validation Scope And Completion |
