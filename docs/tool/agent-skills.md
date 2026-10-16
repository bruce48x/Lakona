# Lakona Project Agent Skills

## Purpose

Lakona projects contain framework-specific development work that is easy to
describe but repetitive to perform. Agent Skills should capture that procedural
knowledge without turning `Lakona.Tool` or `Lakona.Hub` into another package
manager.

The public Skill Pack covers RPC contract definition, RPC service
implementation, Application HTTP services, actor implementation,
HotfixComponent tools and resource lifecycles, framework-owned timers, Game
Session lifecycle policy, generated Game client integration, and advisory server
code organization. New Skills must be justified by observed project work rather
than added as a speculative catalog.

## Distribution Decision

New Lakona projects include the official Skill Pack by default:

- Lakona maintains the canonical Skills under `skills/<skill-name>/` in the
  Lakona Git repository.
- ProjectSystem embeds that complete public tree into both Tool and Hub release
  artifacts.
- Project creation writes it to `.agents/skills/<skill-name>/` through the same
  validated, transactional generation plan as project source and documentation.
- The generated project README explains that `.agents/skills/` should be
  committed so every developer and CI agent sees the same instructions.
- Project creation does not require Node.js, network access, or a second
  installation command.

Tool and Hub do not implement separate Skill installers. The ProjectSystem
generation module owns the embedded resources, paths, validation, and write
behavior behind the unchanged project-creation interface.

## Product Responsibilities

### Lakona Repository

The repository owns:

- canonical Skill source and reference material
- review of Skill changes alongside the framework APIs they describe
- validation that every published Skill has valid metadata and only references
  current public APIs for its declared compatibility line
- release guards that require both Tool and Hub versions to change whenever the
  public Skill Pack changes

Keeping Skill source beside framework source is intentional. It avoids a
second repository, release workflow, issue tracker, and access-control surface,
and makes an API change and its procedural guidance reviewable together.

### Lakona.Tool

`lakona-tool new` remains a project generator, not a general Skill manager. Its
ProjectSystem plan includes the bundled Skill Pack in every new project.
Commands that operate on an existing project must not silently mutate its Agent
configuration.

### Lakona Hub

Hub creates projects through the same ProjectSystem interface, so its new
projects receive the identical bundled Skill Pack. Import and inspection remain
read-only. Hub must not infer compatibility from an Agent directory, overwrite
project Skills, check for Skill updates, or delete Skill files.

## Compatibility And Reproducibility

Skill compatibility belongs to the generated project, not to the machine that
later opens it.

For a new project, one Tool or Hub release owns both the literal Lakona package
versions and the bundled Skill snapshot, so those outputs form one compatible,
reproducible generation result. The installed Hub version and globally
installed Tool version remain unreliable compatibility signals for an existing
project.

The project copy is authoritative after creation. Bundling does not imply
automatic upgrades: older Tool or Hub releases intentionally keep generating
their matching older package and Skill snapshot. Existing-project Skill
installation, synchronization, update, deletion, provenance tracking, and
conflict handling remain outside the current contract.

Skills keep independent trigger and workflow boundaries, but the official
Lakona Skill Pack is released as one snapshot. Individual Skills do not have
independent semantic versions or dependencies.

## Reading Instructions

Generated `AGENTS.md` and `CLAUDE.md` have the same instruction body and differ
only in their heading. Both direct agents to relevant sections of the project
README and applicable scoped instructions. Reading either satisfies the entry
step. Reuse material already available in the current context unless it has
changed; load more when scope expands or needed context is missing. This also
applies to reading steps in Skills, without waiving applicable project rules.

## Shared Validation Policy

Service and layout guidance distinguishes DI-owned `[HotfixComponent]` helpers
from business exceptions. Direct and indirect `System.Exception` subclasses may
remain in Hotfix without a role; construct them with runtime data using `new`.
Do not suggest component markers, DI registration, or moving exceptions to App
to resolve old `LAKONA20037` guidance. Upgrade affected framework packages,
remove mistaken markers (`LAKONA20060`) and temporary suppressions, and verify
compilation and Hotfix loading. This exception does not relax ordinary class
roles or dependency checks for real components.

Each public Skill must remain usable in a generated project without this
repository's contributor docs. Its validation guidance selects checks for the
affected contracts, reuses existing coverage, and adds tests only where
meaningful coverage is missing. Non-behavioral, low-impact edits need relevant
static checks rather than implementation-mirroring tests. One build or test
command may cover several affected projects when it exercises the required
dependency graph and configuration. After relevant checks and applicable stage
gates pass, further verification requires new changes, failures, or a concrete
unresolved risk. Preserve real runtime evidence where the changed contract
requires it and report any unverified outcomes.

Public Skills and their references contain reusable project workflows and
self-contained examples whose illustrative names can be adapted to the target
project. Keep sample-specific class mappings and repository maintenance or
implementation history in framework docs or sample documentation. Local Skill
links must resolve inside the bundled pack; framework contributor scripts,
internal test suites and source-checkout paths must not be prerequisites for
using a Skill in a generated project.

## Public Skill Responsibilities

The canonical workflows, API examples and validation guidance live in the
Skills shipped with each project. This document owns distribution and
maintenance policy rather than a second copy of those workflows.

| Skill | Responsibility |
| --- | --- |
| [Define RPC contract](../../skills/lakona-define-rpc-contract/SKILL.md) | Shared contracts, stable wire IDs and serializer-compatible DTO evolution. |
| [Implement RPC service](../../skills/lakona-implement-service/SKILL.md) | Generated Hotfix binding, service-scoped call contexts and concurrent request coordination. |
| [Implement HTTP service](../../skills/lakona-implement-http-service/SKILL.md) | Hotfix-owned routes/handlers, listener exposure, raw-body security and durable acceptance. |
| [Implement component](../../skills/lakona-implement-component/SKILL.md) | Concurrent tools, resource registration, first Start/final Stop and optional stable State. |
| [Implement actor](../../skills/lakona-implement-actor/SKILL.md) | Stable Actor state, Hotfix Behavior, serialized turns, placement and actor lifecycle. |
| [Implement timer](../../skills/lakona-implement-timer/SKILL.md) | Actor-owned scheduled work and stable timer arguments. |
| [Implement Session lifecycle](../../skills/lakona-implement-session-lifecycle/SKILL.md) | Per-session disconnect, recovery and final cleanup policy. |
| [Integrate Game client](../../skills/lakona-integrate-game-client/SKILL.md) | Generated client ownership, connection progress, business login, recovery and disposal. |
| [Organize server](../../skills/lakona-organize-server/SKILL.md) | Assembly ownership, component identity/restart boundaries and project-owned directory choices. |

Service and HTTP workflows use the component Skill when resource lifecycle work
is required by the authorized task; this handoff does not require renewed
approval. Concrete HotfixComponent dependencies own business resource operations.
Stable infrastructure interfaces remain valid for configuration, secrets and
third-party clients. Prefer the component's Hotfix ComponentConfigureServices
entry for stable root-DI resource registration; App AddServices remains valid for
global host composition. See [Hotfix Components](../hotfix/components.md) for the
framework contract and the Skill's [resource examples](../../skills/lakona-implement-component/references/resource-patterns.md)
for complete authoring patterns. The framework guide's
[Agar example](../hotfix/components.md#agar-example) maps those patterns to the
runnable sample.

The client Skill observes the generated facade's actual `ConnectionStateChanged`
event and initial `ConnectionState`, separately from authentication and session
phase. It detects compatibility from the project's dependencies and generated
output and upgrades a matching dependency set if required members are absent;
it must not invent an event or infer APIs from the installed Tool/Hub version.

## Maintenance Checks

Reuse the existing ProjectSystem embedded pack and transactional writer. Keep
these focused checks alongside the affected Skill changes:

- Repository Guards validate public metadata names, default-prompt skill targets,
  bundled local references and removed Module API/Skill identifiers.
- Generator/analyzer tests compile the component Skill's complete C# resource
  examples directly from its reference document with the real App/Hotfix roles.
- Project creation tests compare the entire emitted Skill Pack with canonical
  source; release smoke checks verify the packed Tool and Hub artifacts.
- Relevant document/link checks cover this authority and changed references.
- Framework lifecycle/hosting changes also select regression coverage for shared
  rollback deadlines, initial publication failure, partial root-factory cleanup,
  prepared-assembly unload and shutdown ordering. These framework checks do not
  require application users to have the Lakona source checkout.

These checks do not replace runtime evidence for changed framework behavior.
Choose affected failure paths from the component Skill and existing lifecycle
coverage; do not require every failure scenario for an unrelated content edit.
Public Skill changes advance both Tool and Hub versions because both ship the
same embedded snapshot. No new installer, per-Skill dependency system or
existing-project synchronization is needed.

## Acceptance Criteria

Bundled distribution is complete when:

- `skills/lakona-define-rpc-contract/SKILL.md`,
  `skills/lakona-implement-service/SKILL.md`,
  `skills/lakona-implement-http-service/SKILL.md`,
  `skills/lakona-implement-component/SKILL.md`,
  `skills/lakona-implement-actor/SKILL.md`,
  `skills/lakona-implement-timer/SKILL.md`,
  `skills/lakona-implement-session-lifecycle/SKILL.md`,
  `skills/lakona-integrate-game-client/SKILL.md`, and
  `skills/lakona-organize-server/SKILL.md` exist in the Lakona repository
- both Tool and Hub release artifacts contain the complete public Skill Pack
- every successfully created project contains the complete public Skill Pack
  under `.agents/skills/`
- Skill files participate in transactional generation and the initial Git
  commit
- generated project documentation explains that the bundled project copy
  should be committed
- public `skills/**` changes require both Tool and Hub release versions to
  change
- a Lakona 1.x project can retain its installed Skill after Lakona 2.x Skills
  are published
- each Skill can complete a representative workflow and validate the Hotfix
  project without relying on a hard-coded sample namespace
