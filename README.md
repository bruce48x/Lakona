# Lakona

[![Tests](https://github.com/bruce48x/Lakona/actions/workflows/tests-linux.yml/badge.svg)](https://github.com/bruce48x/Lakona/actions/workflows/tests-linux.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Lakona.Tool.svg?label=NuGet)](https://www.nuget.org/packages/Lakona.Tool)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com)
[![.NET Standard](https://img.shields.io/badge/netstandard-2.1-512BD4.svg)](https://dotnet.microsoft.com)
[![Unity](https://img.shields.io/badge/Unity-2022-000000.svg?logo=unity)](https://unity.com)
[![Godot](https://img.shields.io/badge/Godot-4.6.x-478CBF.svg?logo=godot-engine)](https://godotengine.org)

Build realtime game servers in C#, share code with Unity or Godot clients, and
ship live logic updates without throwing away player state.

Lakona is a C# full-stack game server framework: shared contracts, hotfixable
business logic, actor-based state execution, typed RPC, reliable push, cluster
routing, runtime guardrails, local diagnostics, and project scaffolding in one
product line.

Lakona does not lock your game to MongoDB—or to any other database. Your data
model, transaction boundaries, indexes, and storage choices stay with your
business code, so you can connect the database or data service that fits each
part of the game without reshaping your actors around a framework-owned schema.

![Lakona Hub managing Unity, Godot, and Tuanjie game projects](blog/static/images/lakona-hub.png)

*Create, import, inspect, and open game projects from Lakona Hub.*

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
5. **🌐 Scale out deliberately.** Cluster routing lets actors and sessions be
   addressed across nodes through explicit route directories and node
   messaging, without hiding network cost behind magical remote objects.
6. **🔎 Diagnose live runtime behavior.** Readiness checks catch configuration
   problems before listeners open, framework logs expose runtime decisions, and
   optional loopback local diagnostics show process, hotfix, actor, session,
   and recent event state while the server runs.
7. **🔌 Swap protocols when the game needs it.** Transports and serializers are
   pluggable. Use TCP, WebSocket, KCP, loopback, JSON, or MemoryPack without
   binding gameplay code to one wire format or transport stack.
8. **🗄️ Keep your data model yours.** Lakona's actor runtime is not an ORM and
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
public static partial class RoomBehavior
{
    public static ValueTask<JoinRoomReply> JoinAsync(
        this RoomActor room,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        room.Players.Add(request.PlayerId);
        return new ValueTask<JoinRoomReply>(
            new JoinRoomReply(Accepted: true, room.Players.Count));
    }
}
```

The public `RoomBehavior.JoinAsync` extension method is the actor API exposed by
generated selectors and actor refs. Change that method, rebuild the hotfix
project, and the server reloads it. No restart. No downtime. Clients never see
the hotfix code.

## Hotfix: Reload Logic, Keep State 🔥

Lakona loads hotfix assemblies into a collectible `AssemblyLoadContext`. The
file watcher detects changes, loads the new DLL, rebuilds the dispatch table,
and unloads the old assembly atomically.

The design separates **stable actor state and runtime infrastructure** from
**replaceable business logic**. A live room actor, player actor, or matchmaking
actor can stay owned by the running server while the C# code that evaluates
rules, rewards, matchmaking decisions, or event behavior is replaced.

```csharp
// In Program.cs: register hotfix and file watching.
var hotfixDirectory = ResolveHotfixDirectory("../../../../Hotfix/bin/Debug/net10.0");

builder.Services.AddLakonaGameHotfix(
    new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll"),
    sharedAssemblyNames: ["Shared"]);

builder.Services.AddLakonaGameHotfixFileWatcher();
```

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
var controlSession = await server.StartSessionAsync(
    playerId, controlConnectionId, controlCallback, ct);

var realtimeSession = await server.StartSessionAsync(
    playerId, realtimeConnectionId, realtimeCallback, ct);
```

Your game can keep a reliable session for login, matchmaking, and leaderboard,
plus a low-latency session for input and state sync. Grouping those sessions by
player, character, or room is application state; transport and serializer
choices remain infrastructure decisions, not gameplay architecture decisions.

## Reliable Push

Players disconnect during critical moments: login, matchmaking, room entry, or
settlement. Reliable push delivers important notifications at least once, with
monotonic sequence numbers and duplicate filtering.

Server:

```csharp
await server.PublishReliablePushAsync<IPlayerCallback, MatchFound>(
    session,
    "match_found",
    new MatchFound { RoomId = roomId },
    (callback, sequence, payload, ct) =>
    {
        payload.ReliableSequence = sequence.Value;
        return callback.OnMatchFound(payload);
    });
```

Client:

```csharp
await client.ProcessReliablePushAsync(
    sequence,
    payload,
    apply: (MatchFound p, CancellationToken ct) =>
    {
        // Handle the message.
        return Task.CompletedTask;
    },
    acknowledge: ack => client.AcknowledgeAsync(ack));
```

The inbox tracks the highest acknowledged sequence, detects gaps, and requests
replay automatically.

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
public static partial class RoomBehavior
{
    public static ValueTask<JoinResult> JoinAsync(
        this RoomActor room,
        JoinRequest request,
        CancellationToken ct = default)
    {
        room.Players.Add(request.PlayerId);
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

Lakona provides in-memory directories for development and SQL-backed node
directory storage for production-oriented deployments. The cluster model keeps
remote routing explicit, so latency, backpressure, route ownership, and node
failure remain visible engineering decisions.

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
- `Lakona.Game.Server` for server hosting, actors, sessions, reliable push,
  health checks, and guardrails
- `Lakona.Game.Client` for engine-neutral client helpers
- `Lakona.Game.Abstractions` for shared framework primitives
- `Lakona.Game.Cluster`, `Lakona.Game.Cluster.Rpc`, the
  `Lakona.Game.Cluster.Rpc.Transport.*` and
  `Lakona.Game.Cluster.Rpc.Serializer.*` adapters, and
  `Lakona.Game.Cluster.Sql` for optional cluster routing, node messaging,
  serialization, and legacy persistence adapters
- `Lakona.Game.Server.Hotfix.*` for hotfix runtime and generators
- `Lakona.Game.Server.Generators` for generated actor APIs
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
- [RPC](docs/rpc.md)
- [Runtime Guardrails](docs/guardrails.md)
- [Use Lakona Observability](https://bruce48x.github.io/Lakona/posts/observability/)
- [Changelog](CHANGELOG.md)

## Contributing

Contributor rules, package boundaries, testing expectations, and release policy
live in [CONTRIBUTING.md](CONTRIBUTING.md).
