# Lakona Project Agent Skills

Status: bundled project-scoped distribution
Date: 2026-07-29
Audience: Lakona.Tool, Lakona.Hub, project-generation, and Skill maintainers

## Purpose

Lakona projects contain framework-specific development work that is easy to
describe but repetitive to perform. Agent Skills should capture that procedural
knowledge without turning `Lakona.Tool` or `Lakona.Hub` into another package
manager.

The public Skill Pack covers RPC contract definition, RPC service
implementation, Application HTTP services, actor implementation,
application-resource modules, framework-owned timer implementation, Game
Session lifecycle policy, and advisory server code organization. Future Skills
must be justified by observed project work rather than added as a speculative
catalog.

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

## Initial Skill: `lakona-implement-service`

### Trigger

Use `lakona-implement-service` when a developer has created or changed an
interface marked with `[RpcService]` in the project's Shared assembly and wants
to create or update its server implementation.

The Skill is not a source generator. It guides an agent through a repository-
aware implementation task in which business behavior still requires judgment.

### Required Workflow

The Skill must instruct the agent to:

1. Read the project's `AGENTS.md`, root README, and any instructions that apply
   to `Shared` and `Server` before editing.
2. Locate the selected `[RpcService]` interface, its `[RpcMethod]` members,
   request and reply DTOs, notification contract, and contract IDs.
3. Find the Hotfix project and inspect neighboring service implementations,
   generated namespaces, actor boundaries, dependency-injection conventions,
   and tests. Project evidence takes precedence over a generic template.
4. Determine whether an implementation already exists. Update it in place and
   preserve intentional business logic; never create a second
   `[HotfixService]` for the same contract.
5. Place a new implementation under `Server/Hotfix/<Domain>/` by default and
   annotate it with `[HotfixService(typeof(I...Service))]`.
6. Preserve each RPC method's name and `ValueTask` return shape while replacing
   its contract request parameter with the call-context type required by that
   project version. Use `call.Request` for the request and use connection,
   session, callback, service-provider, or actor context only when the behavior
   requires it.
7. Prefer the service-specific generated call context, such as
   `StageServiceCall<GetStageProgressRequest>`, when the project generates one.
   Use the generic `HotfixServiceCall<TRequest>` form only when that is the
   established contract for the detected Lakona version and project settings.
8. Reuse existing constructor injection and actor APIs. Do not put mutable game
   state into the service merely to make the method compile.
9. Implement behavior that can be established from contracts, neighboring
   code, tests, and the developer's request. Do not turn an unknown business
   decision into a fake successful reply, an empty DTO, or silent no-op. Ask for
   the missing decision when repository evidence cannot resolve it.
10. Build the Hotfix project and run focused tests when they exist. Report the
    exact files changed and any behavior that still requires a product decision.

### Signature Example

Given a Shared contract:

```csharp
[RpcService(RpcContractIds.Services.Stage)]
public interface IStageService
{
    [RpcMethod(RpcContractIds.StageServiceMethods.GetProgressAsync)]
    ValueTask<GetStageProgressReply> GetProgressAsync(GetStageProgressRequest request);
}
```

A current generated-call project uses this service shape:

```csharp
[HotfixService(typeof(IStageService))]
internal sealed class StageService
{
    public ValueTask<GetStageProgressReply> GetProgressAsync(
        StageServiceCall<GetStageProgressRequest> call)
    {
        // Implement from the project's state and actor model.
    }
}
```

The example is illustrative, not a literal template. Older or differently
configured projects may require `HotfixServiceCall<GetStageProgressRequest>`.
The Skill must detect that distinction from the project instead of assuming the
newest Lakona API.

### Validation Contract

The minimum validation is:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

Use the actual discovered Hotfix project path when a project has renamed its
roots. A successful build must verify, through Lakona's analyzers and source
generators, that:

- exactly one Hotfix implementation binds the RPC contract
- method names, request types, return types, and call contexts match
- referenced Shared, App, actor, and generated types are valid

The Skill should run focused service or domain tests in addition to the build
when the repository provides them. Constructor dependency availability and
runtime binding must be covered by an existing startup or integration check
when they cannot be established statically. The Skill must not claim that a
compiling service has correct business behavior when no behavioral test or
evidence supports that claim.

### Non-Goals

The Service Skill does not:

- invent or renumber RPC contract IDs
- redesign Shared DTOs without an explicit request
- generate client code or invoke RPC from a client
- move actor state into Hotfix services
- create deployment configuration
- install or update Agent Skills
- bypass analyzers or suppress diagnostics to obtain a successful build

## Additional Initial Skills

### `lakona-implement-http-service`

Use this Skill when a developer adds or changes a Lakona Application HTTP
service for operations, webhooks, payment callbacks, or other product
request/response ingress. It owns the workflow across the stable `Server.App`
contract, generated binding, reloadable Hotfix handler, physical listener
exposure, cancellation, security policy, idempotency, and focused validation.

It must keep Application HTTP separate from RPC and Management HTTP, preserve
exact raw bytes for signature verification, treat route shape as stable
protocol, return materialized responses, and route durable acceptance through
an application-owned store or authoritative actor.

### `lakona-organize-server`

Use this Skill when a developer audits, explains, or reorganizes the server
directory tree. It protects Lakona's Shared, stable App, Hotfix, and generated
ownership boundaries while treating feature-first, layer-first, hybrid, and
project-specific folder choices as user-owned design.

It must distinguish hard framework boundaries from soft change-locality
heuristics, use the project's domain vocabulary, present valid alternatives,
and preserve the user's choice. Generic folders such as `Services`,
`Contracts`, and `State` are evidence to inspect rather than automatic
violations.

### `lakona-implement-module`

Use this Skill when a developer adds or revises an `ILakonaModule` for a stable,
process-scoped resource such as PostgreSQL, Redis, a cache, a queue, or an
application-owned background worker. It owns the workflow across synchronous
DI declaration, asynchronous initialization, readiness gating, partial-start
cleanup, graceful stop, and node-scoped configuration.

It must keep lifecycle modules out of business constructors, publish runtime
dependencies through the final root provider, distinguish absent configuration
from an unhealthy configured dependency, and validate real startup when
external connectivity affects Ready.

### `lakona-define-rpc-contract`

Use this Skill when a developer adds or evolves a Shared RPC service,
notification contract, DTO, or numeric contract ID. It owns stable wire IDs,
method and notification shapes, serializer-compatible DTO evolution, Unity
compatibility, and contract-generator validation.

It must keep server runtime types out of Shared, reserve published IDs instead
of reusing them, preserve serialized member order, and leave implementation to
the Service Skill.

### `lakona-implement-actor`

Use this Skill when a developer creates or changes long-lived mutable game
state. It owns the workflow across the stable `Server.App` actor state shell,
stable message DTOs, reloadable `[HotfixBehaviorOf]` methods, actor lifecycle,
generated selectors, placement choice, and focused validation.

It must preserve the stable-state/Hotfix-behavior boundary, use business actor
keys, distinguish `Local`, `Route`, and `Startup`, and use `ActorHosting` rather
than making ordinary calls create actors implicitly.

### `lakona-implement-timer`

Use this Skill when a developer adds or changes scheduled Hotfix work. It owns
stable timer arguments, stable `TimerId` ownership, `[HotfixTimer]` callbacks,
typed callback selectors, one-shot and periodic creation, actor lifecycle
integration, destruction, serialization, and focused validation.

It must use `LakonaTimer`, create and destroy timers inside an active Hotfix
execution scope, keep callbacks thin, and route mutable work into its actor or
application-service owner.

### `lakona-implement-session-lifecycle`

Use this Skill when a developer defines what happens when a resumable Lakona
Game Session disconnects, reconnects, expires, or is explicitly terminated. It
owns the unique `IGameSessionLifecycle` Hotfix binding, stale-event protection,
control/realtime independence, durable cleanup routing, and lifecycle tests.

It must distinguish an RPC connection from a Game Session and a product Player
Session, retain recoverable state during the resume window, and perform
irreversible cleanup only under an explicit product policy.

## Acceptance Criteria

Bundled distribution is complete when:

- `skills/lakona-define-rpc-contract/SKILL.md`,
  `skills/lakona-implement-service/SKILL.md`,
  `skills/lakona-implement-http-service/SKILL.md`,
  `skills/lakona-implement-module/SKILL.md`,
  `skills/lakona-implement-actor/SKILL.md`,
  `skills/lakona-implement-timer/SKILL.md`,
  `skills/lakona-implement-session-lifecycle/SKILL.md`, and
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
