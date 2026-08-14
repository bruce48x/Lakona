# Standards Conformance Checklist

Rule inventory for Pass 8 of `lakona-architecture-review`. This file is an
**index of checkable rules, not a restatement of them**: read each cited
section before checking it. If a cited document renames a section or changes a
rule, update the pointer here in the same change — a stale pointer must fail
loudly, never drift silently.

Each family is one top-level area in the Pass 8 coverage ledger. For every
family record: scanned count, finding count, and the `Not applicable`
justification when repository evidence proves the delta cannot affect it.
Every finding must quote the offending hunk and cite the rule file and
section.

## CONTRIBUTING.md

Source: `CONTRIBUTING.md`, sections `Standard Workflow` /
`Before committing` checklist.

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| CT-01 | Does the delta keep each change scoped to its task, with an inspected staged diff and no unrelated refactors? | Before committing, bullet 1 |
| CT-02 | Does the delta update every affected authority document in the same change when it alters architecture, configuration, public APIs, generated output, or runtime contracts? | Before committing, bullet 4 |
| CT-03 | Does the delta avoid committing generated RPC glue, build output, editor caches, `Library`, `Temp`, `.godot`, `.import`, `bin`, or `obj`? | Before committing, bullet 5 |
| CT-04 | Does the delta apply package-version rules to modified shippable content, and did the release-version guards pass over the accumulated release range? | Before committing, bullets 6–7 |

## Package Boundaries

Source: `docs/contributing/engineering.md`, section `Package Boundaries`.

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| EN-01 | Does `Lakona.Rpc.Core` stay free of concrete transport/serializer/client/server/Unity/Godot dependencies, with no consumer-naming `InternalsVisibleTo`? | Package Boundaries, bullets 1–2 |
| EN-02 | Do `Lakona.Rpc.Client`/`Lakona.Rpc.Server` depend only on `Lakona.Rpc.Core`? | Package Boundaries, bullet 3 |
| EN-03 | Do transport packages own transport behavior and serializer packages own serialization, with no transport/session/dispatch leakage across the seam? | Package Boundaries, bullet 4 |
| EN-04 | Does `Lakona.Game.Server.Actors` stay a process-local actor runtime on its internal mailbox, without distributed-platform ambitions? | Package Boundaries, bullet 5 |
| EN-05 | Does `Lakona.Game.Server` keep cluster contracts, membership, routing, and the fixed TCP + MemoryPack RPC implementation, with `Lakona.Game.Cluster` remaining a domain namespace only? | Package Boundaries, bullet 6 |
| EN-06 | Does the Hotfix boundary hold — App references `Lakona.Game.Server`, Hotfix references App, one collectible load context, no separate Hotfix abstractions assembly/package? | Package Boundaries, bullet 7 |
| EN-07 | Does `Lakona.Game` own reusable infrastructure while game projects own accounts, matchmaking policy, room rules, gameplay, persistence schema, UI, and product DTOs? | Package Boundaries, bullets 8–9 |
| EN-08 | Does `Lakona.ProjectSystem` own project inspection/creation, with `Lakona.Tool` and `Lakona.Hub` staying user-facing adapters and no parallel project generators? | Package Boundaries, bullets 10–12 |
| EN-09 | Are shared contracts kept authoritative, with no server-local duplicate copies? | Package Boundaries, final sentence |

## Contributor Guardrails

Source: `docs/contributing/engineering.md`, section `Contributor Guardrails`.

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| EN-10 | Is every unrelated refactor in the delta avoided unless required for a safe solution? | Contributor Guardrails, bullet 1 |
| EN-11 | Are package README files user-facing, with maintainer rationale in current `docs/**` authorities rather than blog posts or completed implementation plans? | Contributor Guardrails, bullet 2 |
| EN-12 | Is `docs/plans/**` used only for temporary active plans, reviews, and handoffs — durable rules moved to an authority document and completed artifacts deleted? | Contributor Guardrails, bullet 3 |
| EN-13 | Does the delta avoid reintroducing removed branding, old package names, or migration history without an active compatibility reason? | Contributor Guardrails, bullet 4 |
| EN-14 | Does the delta avoid reintroducing scaffolded `Generated/` folders, Unity editor codegen postprocessors, MSBuild codegen targets, CLI tool manifests, or committed generated RPC glue? | Contributor Guardrails, bullet 5 |
| EN-15 | Is generated code deterministic and IL2CPP-friendly without heavy reflection? | Contributor Guardrails, bullet 6 |
| EN-16 | Does Unity-facing runtime, samples, and shared contracts stay Unity 2022 LTS / C# 9.0 compatible — no `Reflection.Emit`, runtime codegen, or JIT-only behavior? | Contributor Guardrails, bullet 7 |
| EN-17 | Are lifetimes explicit with cancellation-safe loops and clear transport and session ownership? | Contributor Guardrails, bullet 8 |
| EN-18 | Do diagnostics use `ActivitySource`, `Meter`, and events with low-cardinality tags — never actor IDs, payloads, request values, or user identifiers as tags? | Contributor Guardrails, bullet 9 |
| EN-19 | Do all `ValueTask` uses follow the allowed patterns — `return default;`, `new ValueTask<T>(value)`, or `async` methods — with no `ValueTask.CompletedTask` or `ValueTask.FromResult(...)`? | Contributor Guardrails, final bullet |

## Tests

Source: `docs/contributing/testing.md`.

| ID | Checkable question | Source pointer |
| --- | --- | --- |
| TE-01 | Do the delta's tests protect runtime contracts rather than mirror implementation details? | Opening sentence |
| TE-02 | When a delta touches actor messaging/mailbox/lifecycle/tooling, RPC runtime, transports, serializers, starter/tooling, game sessions, cluster, hotfix, or Unity samples, does it add or update the focused tests the coverage table requires? | Coverage table |
| TE-03 | Do Unity tests use NUnit + Unity Test Framework, `[UnityTest]` with `IEnumerator` for async, and `NUnitAssert` aliasing? | Unity paragraph |
| TE-04 | When the delta moves or renames `src/**` files, are the source-scan tests updated in the same change? | Source-scan paragraph |
| TE-05 | For solution runs exceeding local tool timeouts, are test projects executed sequentially with the isolated artifacts root from `scripts/test.ps1`? | Final code block |
