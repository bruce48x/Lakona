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
- a single-node cluster topology for local development
- reliable push services
- a default health/check command that explains the derived runtime state

The default local topology is a single process with generated defaults for the node-directory, route-directory, and gateway. This is still a cluster topology; it is simply collapsed into one process for local development. Project/game features can be added by project code and selected with configuration when needed, and production deployments can split features across nodes without changing the user-facing game code structure.

## Configuration Principle

The canonical configuration and startup model is defined in
[Configuration](../configuration.md).
Generated projects should use `Lakona:Node:Id`,
`Lakona:Sessions:Cleanup:DisconnectedRetentionSeconds`, and
`Lakona:Endpoints[]` with endpoint-local `Serializer` and `RpcServices`.
Startup is the strict zero-template host:

```csharp
using Lakona.Game.Server.Hosting;

return await LakonaGameServer.RunAsync(args);
```

Single-node starter projects omit `Lakona:Feature`; generated defaults are
discovered by convention and explained by the check command.

The generated `appsettings.json` should contain only source values the user can understand and may reasonably change.

It should not contain:

- framework identity flags such as `Hotfix.Enabled` or `Cluster.Enabled`
- implementation paths such as `Hotfix.Directory`
- internal storage selectors such as `ReliablePush.Outbox`
- topology abstractions such as `Node.Profile`
- derived cluster values such as advertised endpoints, bootstrap endpoints, feature descriptors, route lease seconds, or send timeout milliseconds

Reliable Push is enabled by default and generated local configuration should
not include `Lakona:ReliablePush:Enabled`. Users may explicitly set
`Lakona:ReliablePush:Enabled=false` later to opt out; the framework then keeps
the same notification API and degrades delivery to immediate best effort with
no ack or replay.

The default configuration should be:

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "Sessions": {
      "Cleanup": {
        "DisconnectedRetentionSeconds": 30
      }
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "127.0.0.1",
        "Port": 20000,
        "RpcServices": [ "login", "chat" ]
      }
    ]
  }
}
```

For WebSocket transport, the generated endpoint includes the path:

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "Sessions": {
      "Cleanup": {
        "DisconnectedRetentionSeconds": 30
      }
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "json",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "chat" ]
      }
    ]
  }
}
```

## Derived Runtime State

Generated server code should derive the full runtime model from the small configuration surface and project conventions.

From `Lakona:Node:Id`, it derives the local node identity.

From `Lakona:Sessions:Cleanup`, it configures session cleanup policy without
requiring server code changes.

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

- node-directory service
- route-directory service
- gateway service
- project/game services as explicit additions outside the generated default
- in-memory node-directory storage for local development
- loopback or local cluster routing defaults

From reliable push defaults, it derives:

- in-memory short-window outbox
- pending message limit
- replay retention window

Users should not need to edit these derived values in normal local development.

## User-Facing Project Shape

Generated projects should guide users toward three editing areas:

```txt
Shared/Contracts/      RPC and reliable push DTOs
Server/App/            zero-template host metadata, configuration, and actor state shells
Server/Hotfix/         services, actor behaviors, lifecycle reactions, and feature declarations
```

The framework still allows user-owned RPC contracts to live in any compiled shared assembly path and namespace. The generated project uses `Shared/Contracts/<Domain>/` as the recommended convention so new projects have one obvious place for RPC services, notification contracts, DTOs, and named RPC contract IDs.

The generated application should include a small vertical slice that demonstrates:

- login creating a session
- session callback binding
- cluster route registration
- reliable welcome notification
- reconnect with pending reliable push replay
- generated hotfix-backed service binding calling current hotfix code

The generated Chat vertical slice must use the core Lakona.Game runtime model:
RPC enters generated hotfix-backed service binding, the current
`Server/Hotfix` service implementation talks to the pre-created
`ChatRoomActor` state shell, and actor behavior remains reloadable through the
hotfix behavior. The generated project must not use static mutable process
state as the room concurrency model.

The hotfix Chat feature must declare the fixed local room actor explicitly:

```csharp
context.EnsureLocalActor<ChatRoomActor>("chat:global");
```

The user should see Lakona.Game's core capabilities through a working game-server story instead of isolated infrastructure examples.

Generated clients use `Rpc.Generated.LakonaGameClient` as the single connection
entry point. Starter code creates one game client, calls `ConnectAsync`, then
accesses services through `gameClient.Api.Shared.*`. Starter code does not
construct `GameClientHello`, register callback bindings by hand, or expose the
lower-level generated `RpcClient`.

## Health And Check Command

Generated projects should include a check command:

```bash
dotnet run --project Server/App/Server.App.csproj -- --readiness-check
```

The command should print derived runtime state in stable, readable lines:

```txt
cluster: ok single-node
node: ok dev-1
features: ok local generated defaults
hotfix: ok local-build Server.Hotfix.dll
reliable-push: ok pending limit 256, replay window 120s
rpc: ok kcp://127.0.0.1:20000
```

Failures must include actionable repair guidance:

```txt
hotfix: failed local build output not found
fix: dotnet build Server/Hotfix/Server.Hotfix.csproj
```

The check output is where generated projects explain the framework state. The configuration file remains small and focused on source values.

## CLI Direction

The CLI should avoid options that disable Lakona.Game's core identity.

Do not introduce:

```bash
--no-hotfix
--hotfix false
--no-cluster
--cluster false
```

Future topology or deployment choices should be expressed as generation-time intent, not as default runtime JSON complexity. Examples:

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
  where to edit, runtime model, client notes, configuration, and tooling
- `AGENTS.md`: directs AI agents to read `README.md` first
- `CLAUDE.md`: directs Claude to read `README.md` first

The README explains that Cluster, Hotfix, and Reliable Push are defaults. It
should not ask the user to enable them.

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
dotnet run --project Server/App/Server.App.csproj -- --readiness-check
dotnet run --project Server/App/Server.App.csproj
```

without editing `appsettings.json`.

Generated projects should start with minimal required configuration, make
derived runtime state visible through checks, and avoid asking new users to
understand optional infrastructure before the first working slice runs.

The user should understand where to write:

- RPC contracts
- stable server orchestration
- hotfixable rules
- reliable business notifications

without needing to understand internal hotfix assembly paths, reliable push outbox implementation names, or cluster bootstrap internals.
