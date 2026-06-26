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

## Run A Game Server

```csharp
using Lakona.Game.Server.Hosting;

return await LakonaGameServer.RunAsync(args);
```

`LakonaGameServer.RunAsync()` registers the default in-memory session services,
reliable push services, actor runtime, health checks, runtime validation,
hotfix loading, and RPC listeners derived from `Lakona:Endpoints[]`. Replace the
default stores when sessions or pending push records must survive process
restarts.

Configure client-facing endpoints in `appsettings.json`:

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "memorypack",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ]
  }
}
```

Transport, serializer, acceptor, and generated service binding are managed by
the framework from endpoint configuration. Application `Program.cs` should not
hand-write transport or serializer constructors.

When `Lakona:Cluster` is configured, the node-to-node cluster serializer is
selected with `Lakona:Cluster:Serializer`. Do not configure cluster RPC by
calling `UseSerializer` directly in the game server host; keep client-facing
endpoint serializers under `Lakona:Endpoints[]:Serializer` and the cluster
serializer under `Lakona:Cluster:Serializer`.

## Use Actors

Actors are process-local state owners with mailbox-ordered execution. State for
one actor is processed sequentially, so actor fields usually do not need locks.

```csharp
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public readonly record struct RoomId(string Value);

[ActorName("room")]
public sealed class RoomActor : Actor<RoomId>
{
    internal readonly HashSet<long> JoinedPlayers = new();
}

public sealed class JoinRoomRequest
{
    public long PlayerId { get; init; }
}

public sealed class JoinRoomReply
{
    public int PlayerCount { get; init; }
}

// In Server.Hotfix:
[HotfixBehaviorOf(typeof(RoomActor))]
public static class RoomBehavior
{
    public static ValueTask<JoinRoomReply> JoinAsync(
        this RoomActor room,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        room.JoinedPlayers.Add(request.PlayerId);

        return new(new JoinRoomReply
        {
            PlayerCount = room.JoinedPlayers.Count
        });
    }
}

var rooms = provider.GetRequiredService<RoomActors>();
var roomId = new RoomId("alpha");
var nodeId = new NodeId("battle-1");
var request = new JoinRoomRequest { PlayerId = 10001 };

var routed = await rooms.Get(roomId).JoinAsync(request, cancellationToken);
var localOnly = await rooms.Local(roomId).JoinAsync(request, cancellationToken);
var pinned = await rooms.Remote(nodeId, roomId).JoinAsync(request, cancellationToken);
```

For frequent business actor calls, reference `Lakona.Game.Server.Generators` as
an analyzer. It generates typed actor accessors with `Get(id)`, `Local(id)`,
and `Remote(nodeId, id)` selectors for `Actor<TKey>` classes.

## Advanced Local Actor Runtime

`IActorRuntime` remains public for generated code, framework-owned boundary
services, tests, diagnostics, and rare node-local escape hatches. It is
process-local: it does not resolve actor directory placement and it does not
route to another node. Business code should prefer generated selectors so local
versus distributed actor intent stays visible.

Use `TryTell` only when a framework boundary must fail fast on local mailbox
pressure. Use `IActorLifecycle`, mailbox metrics, and state queries for
explicit actor management and diagnostics rather than ordinary gameplay calls.

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

- Runtime validation: run generated projects with `--readiness-check`.
- Message recording: configure the framework default recorder to store recent
  actor dispatch records in an in-memory ring buffer.
- Cluster notifications: use `ClientNotificationRelay` from business nodes to
  send serializable callback commands to the gateway that owns the session.
- Feature startup: use `LakonaGameFeature` classes when a server is composed
  from named startup units.

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
