# Lakona

[![Tests and Publish NuGet](https://github.com/bruce48x/Lakona/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/bruce48x/Lakona/actions/workflows/publish-nuget.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Lakona.Tool.svg?label=NuGet)](https://www.nuget.org/packages/Lakona.Tool)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com)
[![.NET Standard](https://img.shields.io/badge/netstandard-2.1-512BD4.svg)](https://dotnet.microsoft.com)
[![Unity](https://img.shields.io/badge/Unity-2022-000000.svg?logo=unity)](https://unity.com)
[![Godot](https://img.shields.io/badge/Godot-4.6.x-478CBF.svg?logo=godot-engine)](https://godotengine.org)

Build realtime game servers in C#, share contracts with Unity or Godot, and
hot-update logic without losing player state.

Lakona combines typed RPC, actors, reliable push, clustering, diagnostics,
scaffolding, and public Agent Skills—while leaving your database and data model
entirely yours.

For desktop workflows, [Lakona Hub](https://github.com/bruce48x/Lakona/releases) creates, imports,
inspects, and opens projects.

## Why Lakona

Online games need more than a socket library. They need one model for client and
server contracts, one runtime for mutable gameplay state, and one deployment
path that can fix live logic without disconnecting everyone.

Lakona is built around that workflow:

1. **🧩 Share C# between frontend and backend.** Put RPC interfaces, DTOs,
   callback contracts, and named protocol ids in one `Shared` project. The
   server and Unity/Godot clients compile the same source, so protocol drift is
   not a normal part of development.
2. **🔥 Hot-update game logic without losing state.** Keep long-lived mutable state
   in stable runtime-owned types, move replaceable behavior into a hotfix
   assembly, and reload that assembly while the server process keeps running.
   It is pure C# through `AssemblyLoadContext`: no Lua bridge, no JS runtime, no
   custom DSL.
3. **🎭 Model gameplay state with actors.** Rooms, players, matches, lobbies, and
   schedulers can run behind typed actor mailboxes. Each actor owns its state,
   processes messages sequentially, and avoids most lock-heavy shared-memory
   code.
4. **⚡ Start simple.** One CLI command creates a server, hotfix project, shared
   contracts, and Unity or Godot client integration. You can run everything in
   one development process before splitting services for production.
5. **🤖 Give coding agents framework-aware playbooks.** Lakona ships public,
   project-local [Agent Skills](skills) for recurring work such as defining
   contracts, implementing services and actors, managing lifecycle boundaries,
   and organizing server code. The Skills inspect project evidence and preserve
   user choices instead of imposing one generic template.
6. **🌐 Scale out deliberately.** Cluster routing lets actors and sessions be
   addressed across nodes through explicit route directories and node
   messaging, without hiding network cost behind magical remote objects.
7. **🔎 Diagnose live runtime behavior.** Readiness checks catch configuration
   problems before listeners open, framework logs expose runtime decisions, and
   optional loopback local diagnostics show process, hotfix, actor, session,
   and recent event state while the server runs.
8. **🔌 Swap protocols when the game needs it.** Transports and serializers are
   pluggable. Use TCP, WebSocket, KCP, loopback, JSON, or MemoryPack without
   binding gameplay code to one wire format or transport stack.
9. **🗄️ Keep your data model yours.** Lakona's actor runtime is not an ORM and
   does not require MongoDB. Use PostgreSQL, MySQL, MongoDB, Redis, an event
   store, or your own service where each business capability needs it.

## Quick Start ⚡

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack
cd MyGame
dotnet run --project "Server/App/Server.App.csproj"
```

One command creates a project with shared C# contracts, hotfixable server logic,
and a Unity or Godot client ready to connect. Start with one process, then grow
into multi-service and multi-node deployments when the game needs it.

## Agent Skills: Framework Knowledge For Your Coding Agent 🤖

Lakona maintains a public [Agent Skill Pack](skills) beside the framework code.
It turns Lakona's architecture contracts into repository-aware workflows, so a
coding agent can inspect the installed framework version, neighboring code,
generated APIs, tests, and your project's conventions before making changes.

The pack covers:

- defining and evolving shared RPC contracts
- implementing RPC and Application HTTP services
- implementing actors, application-resource modules, and timers
- designing Game Session lifecycle behavior
- auditing or reorganizing server code while preserving project-owned layout
  choices

Every generated project already contains the compatible Skill Pack under
`.agents/skills/` as part of the same transactional generation plan as its
source and documentation. Commit that directory with the project so developers,
CI agents, and coding agents all use the same reviewed guidance. Project
creation needs no Node.js, network access, or second installation command.
See [Lakona Project Agent Skills](docs/tool/agent-skills.md) for the
distribution and compatibility model.

## Shared C#: Define Once 🧩

Server and client share the same network contracts and DTOs. Define RPC
interfaces, callbacks, request/reply payloads, and named contract ids in the
`Shared` project; both sides compile from the same source.

```csharp
// Shared/Contracts/Rooms.cs - compiled for server AND client

[RpcService(ApiName = "room")]
public interface IRoomService
{
    [RpcMethod(1)]
    ValueTask<JoinRoomReply> JoinAsync(JoinRoomRequest request);
}

public sealed record JoinRoomRequest(string RoomId, string PlayerId);

public sealed record JoinRoomReply(bool Accepted, int PlayerCount);
```

The stable server app owns actor state and infrastructure. Hotfix behavior owns
the replaceable game decisions that run inside actor turns.

```csharp
// Server.App/Rooms/RoomActor.cs

public readonly record struct RoomId(string Value);

[ActorName("room")]
public sealed class RoomActor : Actor<RoomId>
{
    internal readonly HashSet<string> Players = new(StringComparer.Ordinal);
}
```

```csharp
// Server.Hotfix/Rooms/RoomBehavior.cs - server-only, hot-reloadable

[HotfixBehaviorOf(typeof(RoomActor))]
public sealed partial class RoomBehavior
{
    public ValueTask<JoinRoomReply> JoinAsync(
        RoomActor self,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        self.Players.Add(request.PlayerId);
        return new ValueTask<JoinRoomReply>(
            new JoinRoomReply(Accepted: true, self.Players.Count));
    }
}
```

Public instance methods on the sealed partial behavior class define the actor
API exposed by generated selectors and actor refs. Change a method and rebuild
the hotfix project; the generated debug reload signal causes the server to load
the new behavior without restarting or moving actor state into the hotfix
assembly. Clients never see the hotfix code.

## Hotfix: Reload Logic, Keep State 🔥

Lakona loads hotfix assemblies into a collectible `AssemblyLoadContext`. The
file watcher detects changes, loads the new DLL, rebuilds the dispatch table,
and unloads the old assembly atomically.

The design separates **stable actor state and runtime infrastructure** from
**replaceable business logic**. A live room actor, player actor, or matchmaking
actor can stay owned by the running server while the C# code that evaluates
rules, rewards, matchmaking decisions, or event behavior is replaced.

Generated `Server/App/Program.cs` calls the `LakonaGameServer` hosting façade
and registers only the selected client-facing transports and serializers. The
façade owns framework composition and lifecycle; generated
`Lakona:Hotfix:DebugWatcher=On` configuration connects local Hotfix builds to
reload through `reload.signal`. See the current
[generation architecture](docs/tool/generation-architecture.md#server-renderers)
instead of hand-assembling Hotfix services or file watchers.

| Capability | Traditional | Lakona |
| --- | --- | --- |
| Language | Lua, JS, or custom DSL | C#, same language as the rest of the server |
| Debugging | Separate debugger, type mismatches at runtime | Same IDE, same debugger, compile-time safety |
| Deploy | Restart server or reload an entire VM | Save file, auto reload while state remains owned by the runtime |
| Registration | Manual dispatch wiring | `[HotfixBehaviorOf]` actor behavior plus generated selectors and wrappers |

## Flexible Networking 🔌

Use one transport, or combine channels for different parts of the game. Control
messages can go over WebSocket while realtime state goes over KCP. JSON is easy
to inspect; MemoryPack is compact and fast. The RPC contracts stay the same.

```csharp
// Business state can explicitly remember the sessions that belong together.
var controlSession = await gameServer.StartSessionAsync(
    playerId, controlConnectionId, ct);

var realtimeSession = await gameServer.StartSessionAsync(
    playerId, realtimeConnectionId, ct);
```

Your game can keep a reliable session for login, matchmaking, and leaderboard,
plus a low-latency session for input and state sync. Grouping those sessions by
player, character, or room is application state; transport and serializer
choices remain infrastructure decisions, not gameplay architecture decisions.

## Reliable Push

Players disconnect during critical moments: login, matchmaking, room entry, or
settlement. Reliable push delivers important notifications at least once, with
monotonic sequence numbers and duplicate filtering.

Server business code publishes through the generated callback surface:

```csharp
var status = clientNotifications
    .ForSession<IPlayerCallback>(session)
    .OnMatchFound(new MatchFound { RoomId = roomId });
```

`LakonaGameClient` owns reliable-push sequencing, duplicate filtering,
acknowledgement, and replay as framework protocol. Game callbacks handle the
typed notification rather than calling inbox or ack APIs directly. Delivery is
at least once when reliable push is enabled on that endpoint; callback behavior
must therefore be idempotent. See [Session Lifecycle](docs/session.md) for the
admission and recovery contract.

## Actor Model 🎭

Gameplay state runs inside actors: single-threaded, mailbox-ordered execution.
Inside an actor turn, state is local and sequential. That makes room logic,
player state, match state, timers, and scheduler workflows easier to reason
about than shared mutable objects spread across threads.

```csharp
[ActorName("room")]
public class RoomActor : Actor<RoomId>
{
    internal readonly HashSet<string> Players = new(StringComparer.Ordinal);
}

[HotfixBehaviorOf(typeof(RoomActor))]
public sealed partial class RoomBehavior
{
    public ValueTask<JoinResult> JoinAsync(
        RoomActor self,
        JoinRequest request,
        CancellationToken ct = default)
    {
        self.Players.Add(request.PlayerId);
        return new ValueTask<JoinResult>(new JoinResult { Accepted = true });
    }
}

// One typed access root generated at compile time.
var actors = provider.GetRequiredService<ActorAccess>();

await actors.Route<RoomActor>(roomId).CallAsync(RoomBehavior.JoinAsync, request, ct); // Directory-routed call
await actors.Local<RoomActor>(roomId).CallAsync(RoomBehavior.JoinAsync, request, ct); // Current node only
await actors.Place<RoomActor>(roomId).EnsureAsync(ct);     // Create through placement policy
```

Public methods on `RoomBehavior` declare the generated actor ref call surface
and own the implementation that runs inside the actor turn.

Lower-level `IActorRuntime` calls, including `call.Actors.AskAsync(...)` in
hotfix services, are process-local. Use generated `Route(id)` selectors for
normal actor calls, `Local(id)` only after the caller has chosen the current
process intentionally, and `Place(id)` when code needs to create or ensure an
actor through the registered placement policy.

Source generators produce one `ActorAccess` root with constrained `Route<TActor>`,
`Local<TActor>`, and `Place<TActor>` selectors. Generated selectors expose
generic `CallAsync` and `PostAsync` helpers
that accept behavior method groups such as `RoomBehavior.JoinAsync`. No
reflection, no string-based dispatch.

## Actor Startup

User-authored hotfix code declares actor startup and placement through a
`[HotfixStartup]` type, so reloadable actor runtime loops stay with reloadable
game behavior. Actor lifecycle hooks use explicit `[ActorStart]` and
`[ActorStop]` methods.

```csharp
[HotfixStartup]
public static class GameHotfixStartup
{
    [HotfixConfigureActors]
    public static void Actors(ActorHostBuilder actors)
    {
        actors.RegisterStartup(
            "matchmaking",
            static _ => ActorStartupPlan.Create<MatchmakingActor>(ActorId.From("default")));
    }
}

public sealed record BattleRuntimeTick(string QueueId);

public sealed class BattleRuntimeTimers
{
    public static ValueTask TickAsync(TimerTick<BattleRuntimeTick> tick)
    {
        // Enter generated actor selectors or services here.
        return default;
    }
}

builder.Services.AddLakonaGame(builder.Configuration);
```

## Runtime Guardrails

Start the server, then ask the health endpoint for readiness:

```bash
dotnet run --project "Server/App/Server.App.csproj" --no-build
curl http://127.0.0.1:20080/_lakona/health/ready
```

Guardrails catch missing endpoints, invalid cluster topology, production profile
violations, and hotfix source misconfiguration. Startup still fails before
opening listeners when validation errors are fatal; the ready endpoint exposes
the same diagnostics to production probes while the process is alive.

## Observability 🔎

Lakona is designed to make server failures inspectable. Framework logging is
configured under `Lakona:Observability:Logging`, startup validation reports
guardrail diagnostics through `/_lakona/health/ready`, and local-admin routes
can be explicitly enabled on the same loopback health listener for runtime
snapshots.

```json
{
  "Lakona": {
    "Observability": {
      "LocalAdmin": {
        "Enabled": true,
        "RequireLoopback": true
      }
    }
  }
}
```

When local admin is enabled, the same `http://127.0.0.1:20080` listener also
serves local diagnostics, including:

- process uptime, working set, and GC heap
- hotfix loaded version, dispatch table size, and last reload status
- actor type counts and aggregate mailbox state
- active, disconnected, terminated, and resumable session counts
- recent warnings and errors from the in-memory diagnostics event buffer

Metrics and traces use standard .NET diagnostics. Actor runtime metrics are
emitted through the `Lakona.Game.Actor` `Meter`, and actor traces use the
`Lakona.Game.Actor` `ActivitySource`. File logging, Prometheus serving, and
trace export are explicit integrations, so startup validation fails clearly if
they are enabled without the matching integration registered.

Read the task-oriented guide:
[Use Lakona Observability](https://bruce48x.github.io/Lakona/posts/observability/).

## Cluster 🌐

Scale beyond a single process when the game is ready. Actors are addressable
across nodes through explicit route directories and node messaging.

```csharp
// Same API, single node or cluster: the directory handles routing.
await actors.Route<RoomActor>(roomId).CallAsync(RoomBehavior.JoinAsync, request, ct);
```

The same replicated, in-process membership and actor-activation control plane
is used for one-node and multi-node deployments. Peer endpoints are discovery
hints; committed membership and placement are not stored in PostgreSQL or
another framework database. Complete quorum loss therefore loses framework
runtime state and requires a new cluster incarnation. See the authoritative
[Cluster](docs/cluster.md) contract for formation, joining, fencing, eviction,
and recovery behavior.

## Your Database, Your Domain 🗄️

Lakona keeps business persistence outside the actor core. The framework does
not turn every gameplay object into a database document, prescribe a universal
repository abstraction, or hide database-specific transactions and queries
behind a lowest-common-denominator API.

Register the data services your game actually needs through normal .NET
composition and call them from your application or hotfix boundary. A player
profile, leaderboard, inventory, analytics stream, and cache can each use the
storage technology that matches its consistency, query, and scale requirements.
Lakona owns the runtime boundaries; your game owns its data model.

## What It Does Not Do

Lakona is infrastructure, not a full game business framework. It does not choose
your account model, matchmaking policy, room rules, gameplay simulation,
persistence schema, database technology, reward model, or UI architecture. That
freedom is intentional: Lakona provides the game-server runtime without making
your business data conform to a framework-owned storage model.

## Packages

The repository publishes small packages under `src/`. Stable entry points are:

- `Lakona.Tool` for `lakona-tool new`
- `Lakona.Game.Server` for server hosting, actors, fixed TCP + MemoryPack
  cluster RPC, sessions, reliable push, hotfix loading and dispatch, health
  checks, guardrails, stable Hotfix contracts, and the Hotfix compiler
  extension
- `Lakona.Game.Client` for engine-neutral client helpers
- `Lakona.Game.Abstractions` for shared framework primitives
- `Lakona.Game.LoadTesting` for headless load-test helpers
- `Lakona.Rpc.*` for RPC core, client/server runtime, transports, serializers,
  and analyzers

Use the package README under each `src/<PackageName>/` directory for
package-specific usage.

## Platform Support

| Platform | Status |
| --- | --- |
| .NET 10 server | Full |
| .NET Standard 2.1 shared/client packages | Full |
| Unity 2022 LTS | Full |
| Godot 4.x .NET | Full |
| Windows / Linux / macOS | Full |

## Samples

Game framework samples:

- [samples/Game.Unity.Agar](samples/Game.Unity.Agar) - Unity client with
  dual-channel WebSocket plus KCP
- [samples/Game.Godot.Chat](samples/Game.Godot.Chat) - Godot .NET
  single-endpoint chat sample
RPC-focused samples:

- [samples/Rpc.Unity.Json.Websocket](samples/Rpc.Unity.Json.Websocket)
- [samples/Rpc.Unity.MemoryPack.Kcp](samples/Rpc.Unity.MemoryPack.Kcp)
- [samples/Rpc.Unity.MemoryPack.Tcp](samples/Rpc.Unity.MemoryPack.Tcp)
- [samples/Rpc.Godot.MixedTransport](samples/Rpc.Godot.MixedTransport)

## Further Reading

- [Design Philosophy](docs/design-philosophy.md)
- [Actor Model](docs/actor.md)
- [Hotfix Architecture](docs/hotfix/architecture.md)
- [Session Lifecycle](docs/session.md)
- [Cluster](docs/cluster.md)
- [RPC](docs/rpc/architecture.md)
- [Runtime Guardrails](docs/guardrails.md)
- [Use Lakona Observability](https://bruce48x.github.io/Lakona/posts/observability/)
- [Changelog](CHANGELOG.md)

## Contributing

Contributor rules, package boundaries, testing expectations, and release policy
live in [CONTRIBUTING.md](CONTRIBUTING.md).
