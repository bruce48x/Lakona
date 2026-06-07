---
title: "Get a Lakona.Game Server Running in 5 Minutes"
date: 2026-05-07T11:20:00+08:00
summary: "Scaffold a C# game server with shared contracts, hot-reloadable game logic, and a Unity or Godot client — one command, then dotnet run."
tags:
  - lakona-game
  - lakona-rpc
  - unity
  - godot
  - dotnet
  - tutorial
categories:
  - Tutorial
---

Lakona.Game is an actor-based C# game server framework built on three ideas: **shared contracts** between server and client, **hot reload** for game logic without server restarts, and **one command to start**.

This guide gets you from zero to a running server with a connected client.

## Prerequisites

Install the **.NET 10 SDK**: https://dotnet.microsoft.com/en-us/download/dotnet/10.0

For a Unity client: Unity 2021.3+, with NuGet package restore after first open.
For a Godot client: Godot 4.x .NET.

That is it. The CLI tool handles the rest.

## Quick Start

```bash
dotnet tool install --global Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport tcp --serializer memorypack
cd MyGame
dotnet run --project "Server/Server/Server.csproj"
```

Open `MyGame/Client` in Unity (or Godot), restore packages, open the default scene, and click Play. You have a running server with a connected client.

If you prefer WebSocket and JSON for easier debugging:

```bash
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

## What You Got

```text
MyGame/
  Shared/
    Shared.csproj              # netstandard2.1 + net10.0 multi-target
    Gameplay/GameRules.cs      # state types and DTOs shared with client
  Server/
    Server.slnx
    Server/
      Server.csproj            # net10.0 — your server entry point
      Program.cs               # hosting, hotfix registration, startup
      Services/                # RPC service implementations
    Hotfix/
      Server.Hotfix.csproj     # hot-reloadable game logic
      Gameplay/                # [HotfixSystemOf] extension classes
  Client/                      # Unity or Godot project
  lakona-game.tool.json
```

**Shared** — network contracts, DTOs, state types. Compiled by both server and client. No duplication.

**Server** — RPC hosting, session management, reliable push, actor runtime. Your service implementations live here.

**Hotfix** — game logic that can be changed and reloaded without restarting the server. Uses `AssemblyLoadContext`.

**Client** — Unity or Godot project. Connects to the server via typed RPC. Same DTOs, same contracts.

## Daily Development Flow

Every feature follows the same path:

1. **Define the contract** in `Shared/Interfaces/`
2. **Implement the service** in `Server/Server/Services/`
3. **Call it from the client** via the typed RPC API

Nothing to synchronize by hand. Change a DTO, rebuild, and both sides see it.

## Hot Reload Game Logic

The scaffolded `GameRulesState` in `Shared/` shows the pattern:

```csharp
// Shared/Gameplay/GameRules.cs — compiled for server AND client

[HotfixState]
public sealed partial class GameRulesState
{
    public GameRuleResult Evaluate(GameRuleInput input)
    {
        // Server: dispatched to the hotfix assembly at runtime
        // Client: calls EvaluateStable directly
        return HotfixDispatch.Invoke<GameRulesState, GameRuleInput, GameRuleResult>(
            nameof(Evaluate), this, input);
    }

    internal GameRuleResult EvaluateStable(GameRuleInput input)
    {
        // Fallback logic available to both sides
        return new GameRuleResult { Accepted = true };
    }
}
```

```csharp
// Server/Hotfix/Gameplay/GameRulesSystem.cs — server-only, hot-reloadable

[FriendOf(typeof(GameRulesState))]
[HotfixSystemOf(typeof(GameRulesState))]
public static class GameRulesSystem
{
    public static GameRuleResult Evaluate(this GameRulesState self, GameRuleInput input)
    {
        // Your live game logic — edit, rebuild the hotfix project, save, reload.
        return self.EvaluateStable(input);
    }
}
```

The hotfix project builds independently. Change `GameRulesSystem`, rebuild `Server.Hotfix.csproj`, and the server picks up the new DLL automatically. No restart. No downtime.

## Add Your First RPC Service

Once the default connection test works, add a real service.

Define the contract in `Shared/Interfaces/`:

```csharp
using Lakona.Rpc.Core;

namespace Shared.Interfaces;

public sealed class GetProfileRequest
{
    public long PlayerId { get; set; }
}

public sealed class GetProfileReply
{
    public long PlayerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Level { get; set; }
}

[RpcService(2)]
public interface IProfileService
{
    [RpcMethod(1)]
    ValueTask<GetProfileReply> GetProfileAsync(GetProfileRequest request);
}
```

Implement it in `Server/Server/Services/`:

```csharp
using Shared.Interfaces;

namespace Server.Services;

public sealed class ProfileService : IProfileService
{
    public ValueTask<GetProfileReply> GetProfileAsync(GetProfileRequest request)
    {
        return new ValueTask<GetProfileReply>(new GetProfileReply
        {
            PlayerId = request.PlayerId,
            DisplayName = $"Player {request.PlayerId}",
            Level = 1
        });
    }
}
```

Call it from the client — the source generator produces the typed API from your interface:

```csharp
var reply = await rpc.Api.Shared.Profile.GetProfileAsync(
    new GetProfileRequest { PlayerId = 10001 });
```

That is the full loop: define once, implement once, call from anywhere.

## Reconnect and Reliable Push

Players disconnect. When they reconnect, the server needs to answer: is the session still valid? Is the state compatible? Can pending notifications be replayed?

Lakona.Game surfaces these outcomes explicitly:

- **Resumed** — state is compatible, session continues, pending pushes replay
- **StateRefreshRequired** — session is valid but local state expired, client refreshes from server
- **StateLost** — server cannot validate the old state, client must start fresh

The point is honesty: not every disconnect can be recovered losslessly. When it cannot, the server tells the client explicitly rather than silently corrupting state.

For notifications that must survive disconnects (match found, room ready, reward granted), use reliable push:

```csharp
// Server: publish with at-least-once delivery
await server.PublishReliablePushAsync<IPlayerCallback, MatchFound>(
    session, GameEndpointName.Control, "match_found",
    new MatchFound { RoomId = roomId },
    (callback, payload) => callback.OnMatchFound(payload));

// Client: process with automatic dedup and gap detection
await client.ProcessReliablePushAsync(sequence, payload,
    apply: (MatchFound p, CancellationToken ct) => { /* handle */ return Task.CompletedTask; },
    acknowledge: ack => client.AcknowledgeAsync(ack));
```

The inbox tracks the highest acknowledged sequence, filters duplicates, and requests replay when gaps are detected. You do not build this bookkeeping yourself.

## Choosing Transport and Serializer

Start with the easiest path:

```bash
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

WebSocket + JSON lets you inspect traffic and debug faster. Once your flow is stable, upgrade:

```bash
lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack
```

KCP + MemoryPack is better for low-latency realtime gameplay but harder to debug. Do not enable it on day one.

## Choosing Persistence

Persistence is optional. The default is no business database — omit `--persistence` for that.

When you need a database:

```bash
lakona-tool new --name MyGame --client-engine unity --persistence postgres
```

Options: `none` (default), `postgres`, `mysql`. The tool generates connection configuration and package references. It does not define your business tables — those belong to your game.

## Files You Maintain

In daily work, you touch these:

| Path | What you put there |
|---|---|
| `Shared/Interfaces/` | RPC interfaces, DTOs, callback contracts |
| `Server/Server/Services/` | RPC service implementations |
| `Server/Hotfix/` | Hot-reloadable game logic |
| `Client/` | Game scripts, UI, scenes |

Do not manually maintain: build output, intermediate files, generated RPC glue.

## FAQ

### Do I need to install a separate starter tool?

No. `lakona-tool new` owns project creation directly.

### Can I set up Lakona.Game.Server by hand?

Yes, but it is not the recommended first path. The `lakona-tool new` command generates a runnable project in one step. Understand the generated structure first, then customize.

### Does Lakona.Game implement matchmaking, rooms, or inventory for me?

No. Lakona.Game provides the infrastructure those features run on: session management, reliable push, actor runtime, cluster routing. Matchmaking rules, room logic, gameplay simulation, and business schemas belong to your game.

## What to Read Next

After the default test works:

1. Add your own RPC service (like the profile example above)
2. Try changing a hotfix method and watch the server reload it
3. Move long-lived state into an actor

- [Reliable Business Push: Why Reliable Transport Is Not Enough](/game/posts/reliable-business-push/)
- [Deploying A Lakona.Game Server To Multiple Linux Machines](/game/posts/deploy-lakona-game-server-linux-multi-machine/)
- [Design Philosophy](https://github.com/bruce48x/lakona/blob/main/docs/design-philosophy.md)
