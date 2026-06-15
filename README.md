# Lakona

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
   session types, and stable state definitions in one `Shared` project. The
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

## Product Layers

Lakona brings RPC, actor execution, and game-server infrastructure into one
repository with clear package boundaries:

- `Lakona.Rpc.*` provides typed RPC, protocol primitives, transports,
  serializers, and analyzers.
- `Lakona.Game.Server.Actors` provides game-facing actor execution backed by an
  internal mailbox kernel.
- `Lakona.Game.*` provides game server hosting, sessions, cluster routing,
  hotfix, client helpers, generators, reliable push, and guardrails.
- `Lakona.Tool` provides `lakona-tool`, the project scaffolding and maintenance
  command.

## Quick Start ⚡

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport tcp --serializer memorypack
cd MyGame
dotnet run --project "Server/App/Server.App.csproj"
```

One command creates a project with shared C# contracts, hotfixable server logic,
and a Unity or Godot client ready to connect. Start with one process, then grow
into multi-service and multi-node deployments when the game needs it.

## Shared C#: Define Once 🧩

Server and client share the same network contracts, DTOs, and state types.
Define them in the `Shared` project; both sides compile from the same source.

```csharp
// Shared/Gameplay/GameRules.cs - compiled for server AND client

[HotfixState]
public sealed partial class GameRulesState
{
    private int _minimumScore = 1;

    public GameRuleResult Evaluate(GameRuleInput input)
    {
        // Server: dispatched to the hotfix assembly at runtime
        // Client: calls EvaluateStable directly
        return HotfixDispatch.Invoke<GameRulesState, GameRuleInput, GameRuleResult>(
            nameof(Evaluate), this, input);
    }

    internal GameRuleResult EvaluateStable(GameRuleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.PlayerId))
        {
            return new GameRuleResult { Accepted = false, Reason = "PlayerId required" };
        }

        return input.Score >= _minimumScore
            ? new GameRuleResult { Accepted = true }
            : new GameRuleResult { Accepted = false, Reason = "Score too low" };
    }
}
```

```csharp
// Server.Hotfix/Gameplay/GameRulesSystem.cs - server-only, hot-reloadable

[FriendOf(typeof(GameRulesState))]
[HotfixSystemOf(typeof(GameRulesState))]
public static class GameRulesSystem
{
    public static GameRuleResult Evaluate(this GameRulesState self, GameRuleInput input)
    {
        // Your live game logic: change this, save, and it reloads automatically.
        return self.EvaluateStable(input);
    }
}
```

Change `GameRulesSystem.Evaluate`, rebuild the hotfix project, and the server
reloads it. No restart. No downtime. Clients never see the hotfix code.

## Hotfix: Reload Logic, Keep State 🔥

Lakona loads hotfix assemblies into a collectible `AssemblyLoadContext`. The
file watcher detects changes, loads the new DLL, rebuilds the dispatch table,
and unloads the old assembly atomically.

The design separates **stable runtime state** from **replaceable business
logic**. A live room, player session, or gameplay state object can stay owned by
the running server while the C# code that evaluates rules, rewards, matchmaking
decisions, or event behavior is replaced.

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
| Registration | Manual dispatch wiring | `[HotfixSystemOf]` attribute plus source generator |

## Flexible Networking 🔌

Use one transport, or combine channels for different parts of the game. Control
messages can go over WebSocket while realtime state goes over KCP. JSON is easy
to inspect; MemoryPack is compact and fast. The RPC contracts stay the same.

```csharp
// Server binds two channels per session.
await server.BindEndpointAsync<IControlCallback>(
    session, GameEndpointName.Control, controlConnectionId, controlCallback, ct);

await server.BindEndpointAsync<IRealtimeCallback>(
    session, GameEndpointName.Realtime, realtimeConnectionId, realtimeCallback, ct);
```

Your game gets a reliable channel for login, matchmaking, and leaderboard, plus
a low-latency channel for input and state sync, with the same session identity
across both. Transport and serializer choices remain infrastructure decisions,
not gameplay architecture decisions.

## Reliable Push

Players disconnect during critical moments: login, matchmaking, room entry, or
settlement. Reliable push delivers important notifications at least once, with
monotonic sequence numbers and duplicate filtering.

Server:

```csharp
await server.PublishReliablePushAsync<IPlayerCallback, MatchFound>(
    session,
    GameEndpointName.Control,
    "match_found",
    new MatchFound { RoomId = roomId },
    (callback, payload) => callback.OnMatchFound(payload));
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
    [ActorMethod("join")]
    public ValueTask<JoinResult> JoinAsync(JoinRequest request, CancellationToken ct)
    {
        _players.Add(request.PlayerId);
        return new(new JoinResult { Accepted = true });
    }
}

// Typed selectors generated at compile time.
var rooms = provider.GetRequiredService<RoomActors>();

await rooms.Get(roomId).JoinAsync(request, ct);            // Distributed
await rooms.Local(roomId).JoinAsync(request, ct);          // Current node only
await rooms.Remote(nodeId, roomId).JoinAsync(request, ct); // Pinned to node
```

Source generators produce `RoomActors` with `Get`, `Local`, and `Remote`
selectors. No reflection, no string-based dispatch.

## Feature Catalog Startup

Assemble server capabilities from ordered features. Run all registered features
in one development process, or select a compact feature set per production
process with `Lakona:Game:Feature`.

```csharp
builder.Services.AddLakonaGame(builder.Configuration, game =>
{
    game.Feature<GatewayFeature>("gateway")
        .RequiresTransport("websocket");

    game.Feature<MatchmakingFeature>("matchmaking")
        .After("gateway")
        .RequiresFeature("gateway");

    game.Feature<RoomFeature>("room")
        .After("matchmaking")
        .RequiresFeature("matchmaking")
        .RequiresTransport("kcp");
});
```

## Runtime Guardrails

Validate configuration before starting:

```bash
dotnet run --project "Server/App/Server.App.csproj" -- --lakona-game-check
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
- `Lakona.Game.Server.Hotfix.*` and `Lakona.Game.Server.Generators` for hotfix
  and generated actor APIs
- `Lakona.Game.Server` for game-facing actor runtime
- `Lakona.Rpc.*` for RPC core, client/server runtime, transports, serializers,
  and analyzers

Use the package README under each `src/<PackageName>/` directory for
package-specific usage.

## Platform Support

| Platform | Status |
| --- | --- |
| .NET 10 server | Full |
| .NET Standard 2.1 shared/client packages | Full |
| Unity 2021.3+ | Full |
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

- [Design Philosophy](docs/game/design-philosophy.md)
- [Feature Catalog Startup](docs/game/feature-role.md)
- [Runtime Guardrails](docs/game/lakona-game-runtime-guardrails.md)
- [Actor Kernel Boundary](docs/game/actor-kernel-boundary.md)
- [RPC Overview](docs/rpc/overview.md)
- [RPC Design Notes](docs/rpc/README.md)
- [Changelog](CHANGELOG.md)

## Contributing

Contributor rules, package boundaries, testing expectations, and release policy
live in [CONTRIBUTING.md](CONTRIBUTING.md).
