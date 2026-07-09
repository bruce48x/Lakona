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

The default local topology is one process with generated defaults for the node-directory, route-directory, gateway, and `Lakona:Cluster` endpoint. It is still the normal cluster model; local development simply starts with one node. Project/game actor hosts can be added by project code and selected with configuration when needed, and production deployments can split actor hosts across nodes without changing the user-facing game code structure.

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

Single-node starter projects omit component selection; generated defaults
are explicit actor host and startup actor declarations checked by the readiness
endpoint.

The generated `appsettings.json` should contain only source values the user can understand and may reasonably change.

It should not contain:

- framework identity flags such as `Hotfix.Enabled` or `Cluster.Enabled`
- implementation paths such as `Hotfix.Directory`
- internal storage selectors such as `ReliablePush.Outbox`
- topology abstractions such as `Node.Profile`
- derived cluster values such as advertised endpoints, bootstrap endpoints, actor host descriptors, route lease seconds, or send timeout milliseconds

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
    "Hotfix": {
      "DebugWatcher": "On"
    },
    "Health": {
      "Http": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 20080,
        "RequireLoopback": true
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
    "Hotfix": {
      "DebugWatcher": "On"
    },
    "Health": {
      "Http": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 20080,
        "RequireLoopback": true
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

Generated projects may omit `Lakona:Cluster`; the framework supplies default
node-to-node cluster values. Templates that emit `Lakona:Cluster` must also
write `Lakona:Cluster:Serializer` from the same `--serializer` choice. That
value drives node-to-node cluster RPC payloads and remote actor payloads; it
does not replace the `LakonaInternalCodec` used by handshake, heartbeat,
reliable push ack, or session termination notice.

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
Server/Hotfix/         services, actor behaviors, lifecycle reactions, actor startup, and timer callbacks
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

The hotfix Chat startup must own the fixed local room actor explicitly:

```csharp
public static void ConfigureActors(ActorHostBuilder actors)
{
    actors.RegisterStartup(
        "chat-room",
        static _ => ActorStartupPlan.Create<ChatRoomActor>(ActorId.From("chat-room/global")));
}
```

The user should see Lakona.Game's core capabilities through a working game-server story instead of isolated infrastructure examples.

Generated clients use `Client.Generated.LakonaGameClient` as the single connection
entry point. Starter code creates one game client, calls `ConnectAsync`, then
accesses services through `gameClient.Api.Shared.*`. Starter code does not
construct `GameClientHello`, register callback bindings by hand, or expose the
lower-level generated `RpcClient`.

## Health Endpoints

Generated projects should enable the independent health HTTP host on loopback:

```json
{
  "Lakona": {
    "Health": {
      "Http": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 20080,
        "RequireLoopback": true
      }
    }
  }
}
```

The generated server exposes:

- `GET /_lakona/health/live`: liveness, HTTP 200 with `{ "status": "ok" }`
- `GET /_lakona/health/ready`: readiness, HTTP 200 when guardrails pass or HTTP 503 with JSON diagnostics when they fail

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
dotnet build Server/Hotfix/Server.Hotfix.csproj
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

without needing to understand internal hotfix assembly paths, reliable push outbox implementation names, or cluster bootstrap internals.
