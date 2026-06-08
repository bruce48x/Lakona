# Lakona.Game.Server

`Lakona.Game.Server` provides .NET server hosting helpers for Lakona.Rpc, actor-kernel-backed game-state execution, session lifecycle, endpoint callback binding, and reliable business push.

It exposes `Lakona.Game.Server.Actors` so room, battle, and service state can run with predictable process-local mailbox behavior on the gateway process.

## Install

```powershell
dotnet add package Lakona.Game.Server
```

## Runtime Guardrails

Lakona.Game.Server provides runtime guardrail diagnostics for framework invariants such as node identity, endpoint shape, hotfix assembly presence, and cluster service graph consistency. Generated projects use these diagnostics through `--lakona-game-check`; server hosts can also register the default rules with `AddLakonaGameRuntimeValidation()`.

## Host Lakona.Rpc servers

Register one or more named RPC server configurators in your gateway process:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server;
using Lakona.Game.Server.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<PlayerService>();
builder.Services.AddLakonaGameServer();
builder.Services.AddRpcServer<ControlPlaneRpcServerConfigurator>();
builder.Services.AddLakonaGameServerGateway();

await builder.Build().RunAsync();
```

Implement `IRpcServerConfigurator` to choose the serializer, transport, and generated service binder:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

public sealed class ControlPlaneRpcServerConfigurator : IRpcServerConfigurator
{
    public string Name => "control";

    public void Configure(LakonaGameServerRpcContext context)
    {
        context.Builder
            .UseSerializer(new MemoryPackRpcSerializer())
            .UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(20000, "/ws", ct));

        PlayerServiceBinder.Bind(
            context.Builder.ServiceRegistry,
            callback => ActivatorUtilities.CreateInstance<PlayerService>(context.Services, callback));
    }
}
```

`Name` only identifies the hosted RPC server inside the process. Register another configurator if you need another endpoint.

## Use Actors

Lakona.Game's server-side actor execution model is built on the internal actor kernel through the public `Lakona.Game.Server.Actors` facade. Register the Lakona.Game server services in the same .NET host that runs your gateway:

```csharp
using Lakona.Game.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLakonaGameServer();
```

Messages for the same actor are processed through one mailbox, so actor state can be written without locks:

```csharp
using Lakona.Game.Server.Actors;

public sealed class RoomActor : Actor
{
    private int _joinedPlayers;

    public int JoinedPlayers => _joinedPlayers;

    public ValueTask JoinAsync(long playerId, CancellationToken cancellationToken)
    {
        _joinedPlayers++;
        return ValueTask.CompletedTask;
    }
}

var provider = builder.Build().Services;
var runtime = provider.GetRequiredService<IActorRuntime>();
await runtime.TellAsync<RoomActor>(
    ActorId.From("room/alpha"),
    static (room, ct) => room.JoinAsync(10001, ct));
```

Use `AskAsync` when the caller needs a response:

```csharp
var count = await runtime.AskAsync<RoomActor, int>(
    ActorId.From("room/alpha"),
    static (room, _) => ValueTask.FromResult(room.JoinedPlayers));
```

Use `TryTell` when a caller must fail fast instead of waiting for mailbox capacity:

```csharp
var result = runtime.TryTell<RoomActor>(
    ActorId.From("room/alpha"),
    static (room, ct) => room.JoinAsync(10002, ct));
```

`ActorTellResult.MailboxFull` means the actor's local mailbox rejected the immediate send. Callers can retry later, shed work, or map the result to cluster backpressure.

The facade also supports explicit actor stop/drain, mailbox metrics, mailbox-delivered timers, and lifecycle hooks:

- `StopAsync(id)` removes an actor after draining its mailbox.
- `StopAsync(id, drainTimeout)` returns `ActorStopOutcome.TimedOut` when the actor cannot drain in time.
- `TryGetMailboxMetrics(id, out metrics)` returns Lakona.Game-owned mailbox metrics without exposing internal actor-kernel runtime types.
- `Actor.Context.RegisterTimer(...)` and `IActorRuntime.RegisterTimer(...)` schedule timer ticks through the actor mailbox.
- Override `OnActivateAsync` and `OnDeactivateAsync` for explicit startup and stop cleanup.

The internal actor kernel is the foundation for actor/mailbox runtime concerns. Lakona.Game.Server exposes it only through its facade and keeps the game-session layer focused on session identity, endpoint binding, reconnect, and reliable push integration.

`ClusterActorDispatcher<TActor>` can adapt an explicit `Lakona.Game.Cluster` actor envelope into the local `IActorRuntime` mailbox and wait for a handler result. `ClusterActorTellDispatcher<TActor>` is the one-way variant that uses `TryTell` and maps local mailbox pressure to `ClusterSendStatus.Backpressure`. Both adapters are intentionally typed and require application-provided handler delegates; they do not expose transparent remote actor references or generated remote actor proxies.

## Main Server API

Register the recommended runtime services with one call:

```csharp
using Lakona.Game.Server;

builder.Services.AddLakonaGameServer();
```

Use `ILakonaGameServer` as the main entry point for sessions, endpoint callback bindings, and reliable push:

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Server;

public sealed class MatchPushService
{
    private readonly ILakonaGameServer _server;

    public MatchPushService(ILakonaGameServer server)
    {
        _server = server;
    }

    public ValueTask<GameSessionKey> LoginAsync(
        string playerId,
        string connectionId,
        IPlayerCallback callback,
        CancellationToken ct)
    {
        return _server.StartSessionAsync(playerId, GameEndpointName.Control, connectionId, callback, ct);
    }

    public ValueTask<long> PublishMatchedAsync(
        GameSessionKey session,
        MatchmakingStatusUpdate payload,
        CancellationToken ct)
    {
        return _server.PublishReliablePushAsync<IPlayerCallback, MatchmakingStatusUpdate>(
            session,
            GameEndpointName.Control,
            "matched",
            payload,
            static (callback, sequence, update, _) =>
            {
                update.ReliableSequence = sequence.Value;
                return callback.OnMatchmakingStatus(update);
            },
            ct);
    }

    public ValueTask ReplayAsync(GameSessionKey session, CancellationToken ct)
    {
        return _server.ReplayReliablePushAsync<IPlayerCallback, MatchmakingStatusUpdate>(
            session,
            GameEndpointName.Control,
            "matched",
            static (callback, sequence, update, _) =>
            {
                update.ReliableSequence = sequence.Value;
                return callback.OnMatchmakingStatus(update);
            },
            ct);
    }

    public ValueTask<ReliablePushAckOutcome> AckAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken ct)
    {
        return _server.AckReliablePushAsync(currentSession, acknowledgedSession, sequence, ct);
    }

}
```

The built-in outbox is process-local and in-memory. Replace `IReliablePushOutbox` with a project-specific implementation when pending pushes must survive process restarts. Use `IGameSessionDirectory`, `IReliablePushOutbox`, `ReliablePushRecord`, and `IReliablePushAckService` directly only when you need lower-level control.

## Use Session Lifecycle Helpers

The main API already registers in-memory session helpers. If you need only the lower-level session services, register them directly:

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

builder.Services.AddLakonaGameServerSessions();
```

`IGameSessionDirectory` stores session identity, endpoint bindings, and opaque typed callbacks. Endpoint names are application data, so `"control"` and `"realtime"` are sample conventions rather than framework requirements.

For reconnect, use `IGameSessionResumeService` so token validation and authoritative state checks stay in one place:

```csharp
using Lakona.Game.Server.Sessions;
using Lakona.Game.Abstractions;

public sealed class PlayerLoginService
{
    private readonly IGameSessionResumeService _resume;

    public PlayerLoginService(IGameSessionResumeService resume)
    {
        _resume = resume;
    }

    public async ValueTask<SessionResumeDecision> ResumeAsync(GameSessionKey session, string token, CancellationToken ct)
    {
        var decision = await _resume.TryResumeAsync(new GameSessionResumeRequest(session, token), ct);

        return decision.Status switch
        {
            SessionResumeStatus.Resumed => decision,
            SessionResumeStatus.StateRefreshRequired => decision,
            SessionResumeStatus.StateLost => decision,
            SessionResumeStatus.Unauthorized => decision,
            _ => decision
        };
    }
}
```

Projects can register `IGameSessionTokenValidator` and `IAuthoritativeSessionStateProbe` to decide whether a reconnect is accepted, requires a snapshot refresh, or must start a new session. Lakona.Game does not define account models, token formats, room snapshots, or gameplay DTOs.

## Feature Catalog Assembly

Compose servers from ordered `LakonaGameFeature` startup units. Develop with every registered Feature in one process, or select a compact Feature set per process with `Lakona.Game:Feature`.

```csharp
public sealed class AuthFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        context.Services.AddSingleton<AuthService>();
    }
}

builder.Services.AddLakonaGame(builder.Configuration, game =>
{
    game.Feature<ClusterFeature>("cluster");
    game.Feature<AuthFeature>("auth")
        .After("cluster")
        .RequiresFeature("cluster")
        .RequiresTransport("websocket");
});
```

```json
{
  "Lakona.Game": {
    "Feature": ["cluster", "auth"],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws"
      }
    ]
  }
}
```

`RequiresTransport(...)` validates against the framework-resolved endpoint catalog. Endpoint transport hosting remains framework-owned.

See `../../docs/feature-role.md` and `../../docs/lakona-game-configuration-startup.md` for details.

## Remote Actor Messaging

Use generated typed actor accessors for frequent actor calls. Local and remote calls expose the same business methods, while `Remote(nodeId, id)` keeps the network boundary visible:

```csharp
var localReply = await rooms
    .Local(roomId)
    .JoinAsync(request, cancellationToken);

var remoteReply = await rooms
    .Remote(nodeId, roomId)
    .JoinAsync(request, cancellationToken);
```

The lower-level `AskRemoteAsync` and `TellRemoteAsync` helpers remain available for custom cluster actor envelope plumbing.

See `../../docs/remote-actor-messaging.md` for details.

## Message Recording

Capture every actor message dispatch for offline debugging. One line to enable:

```csharp
builder.Services.AddMessageRecording();

// Later
var store = provider.GetRequiredService<IMessageLogStore>();
var log = await store.GetLogAsync(ActorId.From("player/alice"));
```

See `../../docs/message-recording.md` for details.

## Actor Runtime Configuration

```csharp
builder.Services.AddLakonaGameServerActors(options =>
{
    options.MailboxCapacity = 4096;
    options.SlowMessageThreshold = TimeSpan.FromSeconds(1);
    options.CallTimeout = TimeSpan.FromSeconds(30);
});

// Query actor lifecycle state
var state = runtime.GetState(actorId);  // Active / Draining / Dead
```
