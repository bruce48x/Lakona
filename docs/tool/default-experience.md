# Lakona.Tool Default Experience

## Purpose

`lakona-tool new` should create a runnable Lakona.Game application, not a set of optional framework modules for the user to assemble.

The generated project must present Lakona.Game's core identity clearly:

- cluster-aware node runtime
- hotfixable business rules
- reliable business push

These capabilities are part of the default application model. The tool should reduce user-facing configuration and expose only values that are both understandable and likely to vary between machines or deployments.

## Default Application Model

Every generated project includes:

- a server host
- a hotfix project
- a shared contract/state project
- a client project
- a default one-node cluster topology for local development
- reliable push services
- default HTTP health endpoints that expose liveness and readiness

The default local topology is one process running an in-memory Membership Table
plus a single-node Actor Directory, gateway services, and a
`Lakona:Cluster` endpoint. Project/game Actor hosts can be added by project code
and selected with configuration. Single-process and distributed deployments use
the same cluster path. Multi-node deployments select PostgreSQL, Redis, or MySQL
Membership and use storage owned and prepared by the deployment environment.

## Configuration Principle

The canonical configuration model is defined in
[Configuration](../configuration.md). The exact generated `Program.cs` shape
is maintained with its renderer contract in
[Generation Architecture](./generation-architecture.md#server-renderers).
Generated projects should use `Lakona:Node:Id`,
`Lakona:Sessions:ResumeWindowSeconds`, and
`Lakona:Endpoints[]` with endpoint-local `Serializer` and `RpcServices`.
Startup remains a thin composition root. The generator writes the selected
client-facing transport and serializer registrations; users do not assemble
unrelated framework services or select a cluster RPC stack.

Single-node starter projects omit component selection. Startup Actor groups
are declared in `HotfixStartup.ConfigureActors`, and readiness waits for their
activation. Multi-node deployments use `Lakona:Node:Roles` and Actor `[NodeRole]` declarations to decide which
Actor kinds each node can host.

The generated `appsettings.json` should contain only source values the user can understand and may reasonably change.

It should not contain:

- framework identity flags such as `Hotfix.Enabled` or `Cluster.Enabled`
- implementation paths such as `Hotfix.Directory`
- internal storage selectors such as `ReliablePush.Outbox`
- topology abstractions such as `Node.Profile`
- derived cluster values such as advertised endpoints, peer endpoints, actor host descriptors, route lease seconds, or send timeout milliseconds

Reliable Push is endpoint-local and disabled unless explicitly enabled.
Generated business endpoints opt in; applications that need best-effort
callbacks leave it disabled. Generated WebSocket endpoints add their required
path. The exact key shapes, defaults, and validation rules belong to
[Configuration](../configuration.md).

Generated projects may omit `Lakona:Cluster`; the framework supplies local
defaults. The selected transport and serializer apply only to client-facing
endpoints. The framework-owned cluster channel is defined in
[Cluster RPC Composition](../cluster.md#cluster-rpc-composition), while the
separate control-message codec is defined in
[Session Lifecycle](../session.md#handshake-gate).

## Derived Runtime State

Generated server code should derive the full runtime model from the small configuration surface and project conventions.

From `Lakona:Node:Id`, it derives the local node identity.

From `Lakona:Sessions:Cleanup:IntervalSeconds`, it configures how often the
mandatory bounded session cleanup scans without requiring server code changes.

From `Lakona:Endpoints[]`, it derives:

- the RPC listener addresses
- the advertised client endpoints
- framework-owned endpoint transport wiring
- framework-owned endpoint serializer wiring
- endpoint-local RPC service exposure

From the generated project structure, it derives the local hotfix source:

- hotfix project: `Server/Hotfix/Server.Hotfix.csproj`
- hotfix assembly: `Server.Hotfix.dll`
- local build output under the hotfix project's target framework directory

From the default local topology, it derives:

- one-process in-memory Membership Table
- a virtual-partition Actor Directory; consecutive Membership views hand moved
  ranges to their new owner, while skipped views recover from surviving exact
  `ActorActivationCatalog` snapshots; incomplete recovery remains unavailable rather than
  being projected as absent
- gateway service
- project/game services as explicit additions outside the generated default
- loopback cluster transport defaults

From reliable push defaults, it derives:

- in-memory short-window outbox
- pending message limit
- replay retention window

Users should not need to edit these derived values in normal local development.

## User-Facing Project Shape

Generated projects should guide users toward three editing areas:

```txt
Shared/Contracts/      RPC and reliable push DTOs
Server/App/            thin host composition, configuration, and actor state shells
Server/Hotfix/         services, actor behaviors, lifecycle reactions, actor startup, and timer callbacks
```

The framework still allows user-owned RPC contracts to live in any compiled shared assembly path and namespace. The generated project uses `Shared/Contracts/<Domain>/` as the recommended convention so new projects have one obvious place for RPC services, notification contracts, DTOs, and named RPC contract IDs.

The generated application includes a compact top-down multiplayer arena that demonstrates:

- name-based login creating or reconnecting an in-memory player session
- server-assigned player ids and deterministic client-side player colors
- client direction input with server-authoritative movement and simulation
- server-pushed world snapshots resolved from each player's current game session
- automatic projectiles, player-versus-player damage, monster spawning and pursuit
- health, score, death, five-second respawn, disconnect presence, and reconnect recovery
- generated hotfix-backed service binding calling current hotfix code

Unity and Godot render the arena entirely from engine-provided drawing primitives;
the generated project contains no external art assets. The Console client provides
headless smoke and load flows for the same contracts.

Generated Unity and Tuanjie desktop players start in a resizable
800×600 window. The default is stored in `Client/ProjectSettings/ProjectSettings.asset`
so it applies to player builds without runtime resolution overrides.

Those Unity-compatible clients install the new Input System and use it as their
only active input backend. Their scene contains an `EventSystem` with
`InputSystemUIInputModule` for UI Toolkit, while gameplay movement is defined in
a generated Input Actions asset with WASD, arrow-key, and gamepad bindings.

Unity generation accepts `--client-engine-version 2022|6.0|6.3` and defaults
to `2022`. The selected value controls both the exact editor version written to
`Client/ProjectSettings/ProjectVersion.txt` and the complete default package
baseline written to `Client/Packages/manifest.json`. Tuanjie and Godot accept
only their current supported versions (`1.6.7` and `4.6` respectively);
Console has no client-engine version.

The generated arena must use the core Lakona.Game runtime model. RPC enters a
generated hotfix-backed service binding, and the current `Server/Hotfix`
implementation talks to the pre-created `GameWorldActor` state shell. The actor
owns all mutable world state and the 20 Hz simulation timer serializes gameplay
decisions. Behavior remains reloadable through hotfix code. The generated project
must not use static mutable process state as the world concurrency model.

The hotfix startup owns the fixed local world actor explicitly:

```csharp
[HotfixStartup]
public static class HotfixStartup
{
    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<GameWorldActor, string>(static context => context.Candidates[0]);
    }
}
```

The user should see Lakona.Game's core capabilities through a working game-server story instead of isolated infrastructure examples.

Generated clients use `Client.Generated.LakonaGameClient` as the single connection
entry point. Starter code creates one game client, calls `ConnectAsync`, then
accesses services through `gameClient.Api.Shared.*`. Starter code does not
construct `GameClientHello`, register callback bindings by hand, or expose the
lower-level generated `RpcClient`.

## Health Endpoints

Generated projects should bind the management HTTP listener on loopback and
enable health and Hotfix admin routes independently. The listener, route
enablement, and loopback policy keys are defined in
[Configuration](../configuration.md#validation).

The generated server exposes:

- `GET /_lakona/health/live`: liveness, HTTP 200 with `{ "status": "ok" }`
- `GET /_lakona/health/ready`: readiness, HTTP 200 when guardrails pass or HTTP 503 with JSON diagnostics when they fail

Generated projects also enable Hotfix admin routes on this listener. Health
and admin routes remain loopback-only by default and do not open a second
HTTP port.

Generated local configuration should set `Lakona:Hotfix:DebugWatcher=On` so
`reload.signal` rebuilds reload the current output directory. The readiness
endpoint is where generated projects expose framework validation state. The
configuration file remains small and focused on source values.


## CLI Direction

The CLI should avoid options that disable Lakona.Game's core identity.

Do not introduce:

```bash
--no-hotfix
--hotfix false
--no-cluster
--cluster false
```

Topology and deployment choices are expressed as generation-time intent, not
as default runtime JSON complexity. Examples:

```bash
lakona-tool new --name MyGame --deploy-profile compose
lakona-tool new --name MyGame --topology split-directory
```

The default command remains:

```bash
lakona-tool new --name MyGame
```

## Documentation Direction

Generated projects include a root `README.md` as the single authority and short
AI-agent entry files:

- `README.md`: project overview, generated options, build and run, project structure,
  where to edit, runtime model, client notes, configuration, tooling, and the
  bundled project-scoped Agent Skill location
- `AGENTS.md`: directs AI agents to read `README.md` first
- `CLAUDE.md`: directs Claude to read `README.md` first

The README explains that Cluster, Hotfix, and Reliable Push are defaults. It
should not ask the user to enable them.

Agent Skill distribution follows [Lakona Project Agent Skills](./agent-skills.md).
ProjectSystem writes the complete official Skill Pack under `.agents/skills/`
as part of the transactional project plan. Generated guidance asks developers
to commit that directory and does not require Node.js or a separate install
step.

## Repository Attributes

Every generated root `.gitattributes` starts with the .NET server profile:
cross-platform LF normalization for C#, MSBuild, JSON, XML, documentation,
PowerShell, shell, and deployment files; CRLF for Windows batch files; the
`csharp` diff driver for C#; and explicit binary treatment for signing and
certificate files.

The selected client engine adds its own profile:

- Unity and Tuanjie keep known text-serialized assets on LF, select
  `unityyamlmerge` for scenes and prefabs, and route common source art, media,
  fonts, models, and Unity packages through Git LFS. The generated README
  reminds developers that the repository-local merge-driver configuration must
  point to the selected editor's UnityYAMLMerge executable.
- Godot keeps `.godot`, `.tscn`, `.tres`, scripts, shaders, and configuration
  text-mergeable, while routing common source assets and Godot binary resource
  formats through Git LFS.
- Console projects retain the .NET server profile without imposing Git LFS.

Generated game-engine README files tell users to run `git lfs install` before
committing LFS-managed assets. The generated starter itself contains no
LFS-managed binary art, so project creation and its optional initial commit do
not depend on Git LFS being installed.

## Git Initialization

After transactionally writing generated files, `lakona-tool new` attempts safe
automatic `git init` and initial commit (`"Initial Lakona project"`). The
initializer skips when:

- Git is not available
- the project is inside an existing parent Git worktree
- the project root already has Git commits
- user.name or user.email is not configured (repo is initialized but no commit)

Project generation succeeds regardless of Git environment failures. The CLI
completion output reports the Git status clearly.

## Contract

A new user should be able to run:

```bash
lakona-tool new --name MyGame
dotnet build Server/Server.slnx
dotnet run --project Server/App/Server.App.csproj
curl http://127.0.0.1:20080/_lakona/health/ready
```

without editing `appsettings.json`.

Generated projects should start with minimal required configuration, make
derived runtime state visible through health endpoints, and avoid asking new
users to understand optional infrastructure before the first working slice
runs.

The user should understand where to write:

- RPC contracts
- stable server orchestration
- hotfixable rules
- reliable business notifications

without needing to understand internal hotfix assembly paths, reliable push outbox implementation names, or cluster formation internals.
