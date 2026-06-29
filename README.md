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
routing, runtime guardrails, and project scaffolding in one product line.

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
6. **🔌 Swap protocols when the game needs it.** Transports and serializers are
   pluggable. Use TCP, WebSocket, KCP, loopback, JSON, or MemoryPack without
   binding gameplay code to one wire format or transport stack.

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

[HotfixActorContract(typeof(RoomActor))]
public interface IRoomActorContract
{
    ValueTask<JoinRoomReply> JoinAsync(
        JoinRoomRequest request,
        CancellationToken cancellationToken = default);
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
        return new(new JoinRoomReply(Accepted: true, room.Players.Count));
    }
}
```

Change `RoomBehavior.JoinAsync`, rebuild the hotfix project, and the server
reloads it. No restart. No downtime. Clients never see the hotfix code.

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

[HotfixActorContract(typeof(RoomActor))]
public interface IRoomActorContract
{
    ValueTask<JoinResult> JoinAsync(
        JoinRequest request,
        CancellationToken ct = default);
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
        return new(new JoinResult { Accepted = true });
    }
}

// Typed selectors generated at compile time.
var rooms = provider.GetRequiredService<RoomActors>();

await rooms.Get(roomId).JoinAsync(request, ct);            // Distributed
await rooms.Local(roomId).JoinAsync(request, ct);          // Current node only
await rooms.Remote(nodeId, roomId).JoinAsync(request, ct); // Pinned to node
```

The contract declares the generated actor ref call surface. `RoomBehavior` owns
the implementation that runs inside the actor turn.

Lower-level `IActorRuntime` calls, including `call.Actors.AskAsync(...)` in
hotfix services, are process-local. Use the generated selectors above when code
should say whether a call is distributed, local-only, or pinned to a specific
node.

Source generators produce `RoomActors` with `Get`, `Local`, and `Remote`
selectors. No reflection, no string-based dispatch.

## Feature Startup

Stable `LakonaGameFeature` is framework infrastructure. User-authored game
feature declarations live in the hotfix assembly as descriptors, so reloadable
actor runtime loops stay with reloadable game behavior.

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);

        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}

builder.Services.AddLakonaGame(builder.Configuration);
```

## Runtime Guardrails

Validate configuration before starting:

```bash
dotnet run --project "Server/App/Server.App.csproj" -- --readiness-check
```

Guardrails catch missing endpoints, invalid cluster topology, production profile
violations, and hotfix source misconfiguration before they reach production.

## Cluster 🌐

Scale beyond a single process when the game is ready. Actors are addressable
across nodes through explicit route directories and node messaging.

```csharp
// Same API, single node or cluster: the directory handles routing.
await rooms.Get(roomId).JoinAsync(request, ct);
```

Lakona provides in-memory directories for development and SQL-backed node
directory storage for production-oriented deployments. The cluster model keeps
remote routing explicit, so latency, backpressure, route ownership, and node
failure remain visible engineering decisions.

## What It Does Not Do

Lakona is infrastructure, not a full game business framework. It does not choose
your account model, matchmaking policy, room rules, gameplay simulation,
persistence schema, reward model, or UI architecture. Those decisions belong to
your game.

## Packages

The repository publishes small packages under `src/`. Stable entry points are:

- `Lakona.Tool` for `lakona-tool new`
- `Lakona.Game.Server` for server hosting, actors, sessions, reliable push,
  health checks, and guardrails
- `Lakona.Game.Client` for engine-neutral client helpers
- `Lakona.Game.Abstractions` for shared framework primitives
- `Lakona.Game.Cluster`, `Lakona.Game.Cluster.Rpc`, and
  `Lakona.Game.Cluster.Sql` for optional cluster routing and persistence
  adapters
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
- [samples/Game.Cluster.TwoNode](samples/Game.Cluster.TwoNode) - Multi-process
  cluster with directory services

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
- [Changelog](CHANGELOG.md)

## Contributing

Contributor rules, package boundaries, testing expectations, and release policy
live in [CONTRIBUTING.md](CONTRIBUTING.md).
