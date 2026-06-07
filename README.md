# Lakona

A C# game server framework. Shared code, C# hot reload, actor execution, typed
RPC, reliable push, runtime guardrails, and project scaffolding in one monorepo.

## What Is Lakona

Lakona is an actor-based distributed game framework for C#. Define your network
contracts and state types in one `Shared` project, write your game logic on the
server, and hot-reload it without restarting the process.

- **Shared contracts.** RPC interfaces, DTOs, session types, and state
  definitions live in one `Shared` project. Server and Unity/Godot clients
  compile the same source with no duplication or drift.
- **Hot reload.** Edit game logic, save, and the server picks it up
  automatically. It uses `AssemblyLoadContext`: pure C#, no Lua, no JS, no DSL.
- **Typed RPC.** Source generators produce client facades, callback binders, and
  server binders without runtime reflection dispatch.
- **Actor execution.** Gameplay state runs through process-local actor mailboxes:
  sequential, bounded, observable, and lock-light.
- **Easy to start.** One CLI command scaffolds a complete project with server,
  hotfix, shared contracts, and client integration.

Lakona brings the former RPC, Actor, and Game layers into one repository:

- `Lakona.Rpc.*` for communication, transports, serializers, analyzers, and
  starter tooling.
- `Lakona.Actor` for process-local actor/mailbox execution.
- `Lakona.Game.*` for game server hosting, sessions, cluster routing, hotfix,
  client helpers, generators, and guardrails.
- `Lakona.Tool` for project scaffolding and maintenance commands.

## Quick Start

```bash
dotnet tool install --global Lakona.Tool
lakona new --name MyGame --client-engine unity --transport tcp --serializer memorypack
cd MyGame
dotnet run --project "Server/Server/Server.csproj"
```

One command creates a project with hot-reloadable game logic, shared contracts,
and a Unity or Godot client ready to connect. No manual wiring.

## Shared Contracts: Define Once

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

## Hot Reload

Lakona loads hotfix assemblies into a collectible `AssemblyLoadContext`. The
file watcher detects changes, loads the new DLL, rebuilds the dispatch table,
and unloads the old assembly atomically.

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
| Deploy | Restart server or reload an entire VM | Save file, auto reload |
| Registration | Manual dispatch wiring | `[HotfixSystemOf]` attribute plus source generator |

## Dual-Channel Networking

Control messages over WebSocket, realtime state over KCP. Built in, not bolted
on.

```csharp
// Server binds two channels per session.
await server.BindEndpointAsync<IControlCallback>(
    session, GameEndpointName.Control, controlConnectionId, controlCallback, ct);

await server.BindEndpointAsync<IRealtimeCallback>(
    session, GameEndpointName.Realtime, realtimeConnectionId, realtimeCallback, ct);
```

Your game gets a reliable channel for login, matchmaking, and leaderboard, plus
a low-latency channel for input and state sync, with the same session identity
across both.

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

## Actor Model

Gameplay state runs inside actors: single-threaded, mailbox-ordered execution.
No locks, no races inside the actor turn.

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
dotnet run --project "Server/Server/Server.csproj" -- --lakona-check
```

Guardrails catch missing endpoints, invalid cluster topology, production profile
violations, and hotfix source misconfiguration before they reach production.

## Cluster

Scale beyond a single process. Actors are addressable across nodes through a
directory service.

```csharp
// Same API, single node or cluster: the directory handles routing.
await rooms.Get(roomId).JoinAsync(request, ct);
```

Lakona provides in-memory directories for development and SQL-backed node
directory storage for production-oriented deployments.

## What It Does Not Do

Lakona is infrastructure, not a full game business framework. It does not choose
your account model, matchmaking policy, room rules, gameplay simulation,
persistence schema, reward model, or UI architecture. Those decisions belong to
your game.

## Packages

The repository publishes small packages under `src/`. Stable entry points are:

- `Lakona.Tool` for `lakona new`
- `Lakona.Game.Server` for server hosting, actors, sessions, reliable push,
  health checks, and guardrails
- `Lakona.Game.Client` for engine-neutral client helpers
- `Lakona.Game.Abstractions` for shared framework primitives
- `Lakona.Game.Cluster`, `Lakona.Game.Cluster.Rpc`, and
  `Lakona.Game.Cluster.Sql` for optional cluster routing and persistence
  adapters
- `Lakona.Game.Server.Hotfix.*` and `Lakona.Game.Server.Generators` for hotfix
  and generated actor APIs
- `Lakona.Actor` for process-local actor runtime
- `Lakona.Rpc.*` for RPC core, client/server runtime, transports, serializers,
  analyzers, and starter tooling

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
- [Actor Boundary](docs/game/lakona-actor-boundary.md)
- [RPC Design Notes](design/rpc/README.md)

## Contributing

Contributor rules, package boundaries, testing expectations, and release policy
live in [CONTRIBUTING.md](CONTRIBUTING.md).
