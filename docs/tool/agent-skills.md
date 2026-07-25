# Lakona Project Agent Skills

Status: public Skill Pack implemented; distribution integration pending
Date: 2026-07-25
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

## V1 Decision

V1 uses a Git-first, project-scoped distribution model:

- Lakona maintains the canonical Skills under `skills/<skill-name>/` in the
  Lakona Git repository.
- The project README, `lakona-tool new` completion output, and Lakona Hub show a
  version-compatible `npx skills add` command.
- The developer runs that command explicitly from the project root.
- The installed Skill is project-local and should be committed with the
  project so every developer and CI agent sees the same instructions.
- Lakona.Tool and Lakona Hub do not execute Node.js, install Node.js, call the
  Skills CLI, or implement native Skill installation and updates in V1.

The upstream command is `npx skills add`, not `npx skills install`. A published
command has this shape:

```powershell
npx skills add https://github.com/bruce48x/Lakona/tree/<compatible-git-ref> --skill '*'
```

The final command may add an agent selector or `--copy` when needed. It must
remain a project-scope installation; generated guidance must not recommend a
global installation.

This design keeps Lakona independent of the Node.js runtime while using the
existing cross-agent installer for users who already have Node.js. A developer
who cannot run `npx` may manually copy the same tagged Skill directory into the
agent's project-level Skills directory.

## Product Responsibilities

### Lakona Repository

The repository owns:

- canonical Skill source and reference material
- review of Skill changes alongside the framework APIs they describe
- compatibility guidance from Lakona package versions to immutable Git refs
- a manual-copy fallback
- validation that every published Skill has valid metadata and only references
  current public APIs for its declared compatibility line

Keeping Skill source beside framework source is intentional. It avoids a
second repository, release workflow, issue tracker, and access-control surface,
and makes an API change and its procedural guidance reviewable together.

### Lakona.Tool

`lakona-tool new` remains a project generator, not a Skill installer. It should:

- include Skill installation and upgrade guidance in the generated root README
- print the recommended command after successful project creation
- select guidance from the generated project's Lakona package versions
- never fail project generation because Node.js or `npx` is unavailable

The Tool must not silently mutate an existing project's Agent configuration.

### Lakona Hub

Hub should expose the same guidance on a project's detail page. In V1 it may:

- inspect literal Lakona package references using the existing read-only
  project inspection boundary
- show the compatible Skill source ref and installation command
- copy the command or open this documentation

Hub must not execute the command or describe the Skill as installed merely
because it detects an Agent directory. Native installation, update checks, and
managed-file deletion are outside V1.

## Compatibility And Reproducibility

Skill compatibility belongs to the project, not to the machine that happens to
run Hub or Tool.

For the initial Skills, the decisive inputs are the project's Game Server, RPC,
Hotfix, and Hotfix generator package references. The installed Hub version and
the globally installed Tool version are not reliable compatibility signals for
an existing project.

Every recommended installation command must identify an immutable Git tag or
commit. Documentation must not tell production projects to install from
`main`. This permits, for example, one project to retain Lakona 1.x guidance
while another uses Lakona 2.x guidance.

Before Lakona 1.0, compatibility may need to follow a package minor line because
minor versions can contain breaking API changes. Starting with Lakona 1.0, the
default compatibility boundary is a package major line. A Skill change that is
compatible with more than one line may share its content, but each published
command still resolves to an immutable source snapshot.

Installation does not imply automatic upgrades. The installed project copy is
authoritative until the developer chooses a newer compatible ref and reviews
the resulting diff. Generic `npx skills update` guidance must not be used when
it could cross a Lakona compatibility boundary. An upgrade is the explicit
reinstallation of a newer compatible tagged Skill followed by a project commit.

Skills keep independent trigger and workflow boundaries, but the official
Lakona Skill Pack has one compatibility ref. V1 does not assign independent
semantic versions or dependencies to individual Skills. One tagged repository
snapshot installs or upgrades the complete compatible pack.

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

## Why V1 Does Not Copy CoplayDev

CoplayDev's `unity-mcp` keeps a canonical `unity-mcp-skill` directory and adds
an Editor window that mirrors it from GitHub. Its implementation reads the
GitHub tree, compares blob hashes, downloads changed files, mirrors deletions,
validates the result, records the synced commit, and guards against truncated
tree responses.

That is a useful future UX reference, but it is already a small update system.
Implementing and maintaining the same behavior in both Lakona.Tool and Lakona
Hub would be disproportionate while Lakona has a small initial Skill Pack and
an existing cross-agent installer is available.

If V1 usage proves that manual installation is a material problem, the next
step is a narrow official-Skills synchronizer behind `Lakona.ProjectSystem`:

```txt
project package versions
  -> compatible immutable Lakona Skill release
  -> preview managed file changes
  -> explicit confirmation
  -> transactional project-local write
  -> validation and recorded provenance
```

The first native implementation should download a tagged release archive and
extract a fixed subtree. It should not begin with a registry, dependency
resolver, third-party catalog, or GitHub Tree API incremental mirror. Tool and
Hub must use the same ProjectSystem plan instead of implementing separate
installers.

## Acceptance Criteria For The Documentation-First V1

V1 guidance is complete when:

- `skills/lakona-define-rpc-contract/SKILL.md`,
  `skills/lakona-implement-service/SKILL.md`,
  `skills/lakona-implement-http-service/SKILL.md`,
  `skills/lakona-implement-module/SKILL.md`,
  `skills/lakona-implement-actor/SKILL.md`,
  `skills/lakona-implement-timer/SKILL.md`,
  `skills/lakona-implement-session-lifecycle/SKILL.md`, and
  `skills/lakona-organize-server/SKILL.md` exist in the Lakona repository
- the complete Skill Pack is installable from one immutable Git ref with
  `npx skills add`
- generated project documentation explains project-scope installation and the
  manual-copy fallback
- Tool completion output and Hub show the same compatible command
- a Lakona 1.x project can retain its installed Skill after Lakona 2.x Skills
  are published
- each Skill can complete a representative workflow and validate the Hotfix
  project without relying on a hard-coded sample namespace

## External References

- [Skills CLI](https://github.com/vercel-labs/skills)
- [CoplayDev canonical Skill directory](https://github.com/CoplayDev/unity-mcp/tree/main/unity-mcp-skill)
- [CoplayDev GitHub-based Skill synchronization design](https://github.com/CoplayDev/unity-mcp/pull/845)
