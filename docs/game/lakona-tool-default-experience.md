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

The default local topology is a single process with generated defaults for the node-directory, route-directory, and gateway. This is still a cluster topology; it is simply collapsed into one process for local development. Project/game services can be added by project configuration or future templates, and production deployments can split services across nodes without changing the user-facing game code structure.

## Configuration Principle

The canonical configuration and startup model is defined in [Lakona.Game Configuration And Startup Model](lakona-game-configuration-startup.md). Generated projects should use `Lakona.Game:Node:Id`, `Lakona.Game:Endpoints[]`, compact `Lakona.Game:Feature` selection when needed, and `AddLakonaGame` Feature Catalog startup.

The generated `appsettings.json` should contain only source values the user can understand and may reasonably change.

It should not contain:

- framework identity flags such as `Hotfix.Enabled`, `Cluster.Enabled`, or `ReliablePush.Enabled`
- implementation paths such as `Hotfix.Directory`
- internal storage selectors such as `ReliablePush.Outbox`
- topology abstractions such as `Node.Profile`
- derived cluster values such as advertised endpoints, bootstrap endpoints, service lists, route lease seconds, or send timeout milliseconds

The default configuration should be:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Host": "127.0.0.1",
        "Port": 20000
      }
    ]
  }
}
```

For WebSocket transport, the generated endpoint includes the path:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws"
      }
    ]
  }
}
```

## Derived Runtime State

Generated server code should derive the full runtime model from the small configuration surface and project conventions.

From `Lakona.Game:Node:Id`, it derives the local node identity.

From `Lakona.Game:Endpoints[]`, it derives:

- the RPC listener addresses
- the advertised client endpoints
- framework-owned endpoint transport wiring

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
Server/Server/Game/    stable business orchestration
Server/Hotfix/         hotfixable business rules
```

The framework still allows user-owned RPC contracts to live in any compiled shared assembly path and namespace. The generated project uses `Shared/Contracts/<Domain>/` as the recommended convention so new projects have one obvious place for RPC services, notification contracts, DTOs, and named RPC contract IDs.

The generated application should include a small vertical slice that demonstrates:

- login creating a session
- endpoint binding
- cluster route registration
- reliable welcome notification
- reconnect with pending reliable push replay
- stable wrapper calling a hotfix rule

The generated Chat vertical slice must use the core Lakona.Game runtime model: RPC enters through `IChatService`, stable server code dispatches to `ChatRoomActor` through `IActorRuntime`, and message filtering calls a reloadable Hotfix rule through a stable wrapper. The generated project must not use static mutable process state as the room concurrency model.

The user should see Lakona.Game's core capabilities through a working game-server story instead of isolated infrastructure examples.

## Health And Check Command

Generated projects should include a check command:

```bash
dotnet run --project Server/Server/Server.csproj -- --lakona-game-check
```

The command should print derived runtime state in stable, readable lines:

```txt
cluster: ok single-node
node: ok dev-1
services: ok node-directory, route-directory, gateway
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

Generated projects should include short, task-oriented documentation:

- `docs/GETTING_STARTED.md`: build, check, run, open client, change first RPC, change first reliable push, change first hotfix rule
- `docs/EDITING_GUIDE.md`: where to edit contracts, stable orchestration, hotfix rules, and deployment settings
- `docs/OPERATIONS.md`: check command, hotfix build/reload model, cluster topology, environment variable overrides

These documents should explain that Cluster, Hotfix, and Reliable Push are defaults. They should not ask the user to enable them.

## Implementation Phases

### Phase 1: Reduce Default Configuration

- Generate the smaller `Lakona.Game` configuration shape.
- Move Hotfix, Reliable Push, and Cluster defaults into generated server code.
- Add a runtime options type that derives full endpoint, cluster, hotfix, and reliable push settings from the small configuration shape.
- Register project Features through the `AddLakonaGame` Feature Catalog so startup order and transport requirements are explicit in code.

### Phase 2: Add Check Output

- Add `--lakona-game-check`.
- Print the derived cluster, node, hotfix, reliable push, and RPC endpoint state.
- Return non-zero on failed checks.
- Include repair guidance for common local-development failures.

### Phase 3: Generate A Business Vertical Slice

- Generate project-level services for sessions, notifications, and hotfix rules.
- Wrap reliable push behind a `GameNotificationService`.
- Generate a stable `GameRulesService` and hotfix rule system.
- Add login and reconnect examples that exercise the defaults.

### Phase 4: Improve Generated Documentation

- Generate task-oriented project docs.
- Update tool completion output to point to the check command and the first editing locations.
- Keep package README files focused on package-level usage and the generated project docs focused on application editing.

## Success Criteria

A new user should be able to run:

```bash
lakona-tool new --name MyGame
dotnet build Server/Server.slnx
dotnet run --project Server/Server/Server.csproj -- --lakona-game-check
dotnet run --project Server/Server/Server.csproj
```

without editing `appsettings.json`.

The user should understand where to write:

- RPC contracts
- stable server orchestration
- hotfixable rules
- reliable business notifications

without needing to understand internal hotfix assembly paths, reliable push outbox implementation names, or cluster bootstrap internals.
