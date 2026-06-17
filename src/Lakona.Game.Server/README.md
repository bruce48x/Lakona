# Lakona.Game.Server

`Lakona.Game.Server` is the server hosting package for Lakona game applications.
It wires together RPC hosting, game sessions, reliable push, actor-backed state,
runtime validation, and optional cluster-facing helpers.

Use this package in the server process that accepts game client connections or
hosts game-side services.

## Install

```powershell
dotnet add package Lakona.Game.Server
```

## Register Services

```csharp
using Lakona.Game.Server;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLakonaGameServer();
builder.Services.AddRpcServer<ClientRpcServerConfigurator>();
builder.Services.AddLakonaGameServerGateway();

await builder.Build().RunAsync();
```

`AddLakonaGameServer()` registers the default in-memory session services,
reliable push services, actor runtime, health checks, and runtime validation
services. Replace the default stores when sessions or pending push records must
survive process restarts.

## Host RPC

Implement `IRpcServerConfigurator` to choose the serializer, transport, and
generated service binder for each hosted endpoint.

```csharp
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;
using Microsoft.Extensions.DependencyInjection;

public sealed class ClientRpcServerConfigurator : IRpcServerConfigurator
{
    public string Name => "websocket";

    public void Configure(LakonaGameServerRpcContext context)
    {
        context.Builder
            .UseSerializer(new MemoryPackRpcSerializer())
            .UseAcceptor(ct => WsConnectionAcceptor.CreateAsync(20000, "/ws", ct));

        PlayerServiceBinder.Bind(
            context.Builder.ServiceRegistry,
            callback => ActivatorUtilities.CreateInstance<PlayerService>(context.Services, callback));
    }
}
```

Register another configurator when the process needs another endpoint.

## Use Actors

Actors are process-local state owners with mailbox-ordered execution. State for
one actor is processed sequentially, so actor fields usually do not need locks.

```csharp
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;

public sealed class RoomActor : Actor
{
    private int _joinedPlayers;

    public ValueTask JoinAsync(long playerId, CancellationToken cancellationToken = default)
    {
        _joinedPlayers++;
        return default;
    }

    public ValueTask<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<int>(_joinedPlayers);
    }
}

var runtime = provider.GetRequiredService<IActorRuntime>();
var roomId = ActorId.From("room/alpha");

await runtime.TellAsync<RoomActor>(
    roomId,
    static (room, ct) => room.JoinAsync(10001, ct));

int count = await runtime.AskAsync<RoomActor, int>(
    roomId,
    static (room, ct) => room.CountAsync(ct));
```

Use `TryTell` when the caller must fail fast on local mailbox pressure. Use
`StopAsync`, `TryGetMailboxMetrics`, and actor lifecycle hooks when you need
explicit actor management.

For frequent business actor calls, reference `Lakona.Game.Server.Generators` as
an analyzer. It generates typed actor accessors with `Get(id)`, `Local(id)`,
and `Remote(nodeId, id)` selectors for `Actor<TKey>` classes.

## Sessions And Push

`ILakonaGameServer` is the high-level entry point for game sessions, typed
callback binding, reliable push, replay, and acknowledgements.

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
        CancellationToken cancellationToken)
    {
        return _server.StartSessionAsync(playerId, connectionId, callback, cancellationToken);
    }

    public ValueTask<long> PublishMatchedAsync(
        GameSessionKey session,
        MatchmakingStatusUpdate update,
        CancellationToken cancellationToken)
    {
        return _server.PublishReliablePushAsync<IPlayerCallback, MatchmakingStatusUpdate>(
            session,
            "matched",
            update,
            static (callback, sequence, payload, ct) =>
            {
                payload.ReliableSequence = sequence.Value;
                return callback.OnMatchmakingStatus(payload);
            },
            cancellationToken);
    }
}
```

Use `IGameSessionResumeService` when reconnects need token validation or an
authoritative state check. Lakona does not define account models, room rules,
matchmaking policy, persistence schema, or gameplay DTOs.

## Optional Features

- Runtime validation: call `AddLakonaGameRuntimeValidation()` or run generated
  projects with `--lakona-game-check`.
- Message recording: call `AddMessageRecording()` to store recent actor
  dispatch records in an in-memory ring buffer.
- Cluster notifications: use `ClientNotificationRelay` from business nodes to
  send serializable callback commands to the gateway that owns the session.
- Feature startup: use `AddLakonaGame(...)` and `LakonaGameFeature` classes
  when a server is composed from named startup units.

## Actor Runtime Configuration

```csharp
builder.Services.AddLakonaGameServerActors(options =>
{
    options.MailboxCapacity = 4096;
    options.SlowMessageThreshold = TimeSpan.FromSeconds(1);
    options.CallTimeout = TimeSpan.FromSeconds(30);
});
```

Actor ids are application-owned strings. Pick stable names such as
`player/alice`, `room/alpha`, or `match/2026-06-17-001` when other services need
to address the same actor.
