# Lakona.Game.Server

`Lakona.Game.Server` is the server hosting package for Lakona game applications.
It wires together RPC hosting, game sessions, reliable push, actor-backed state,
hotfix loading and dispatch, runtime validation, and framework-owned cluster
membership, routing, messaging, and RPC.

Use this package in the server process that accepts game client connections or
hosts game-side services.

Cluster contracts and implementations use the `Lakona.Game.Cluster` namespace
but ship as part of this package; there is no separate `Lakona.Game.Cluster`
package to install or version.

Stable Hotfix contracts live directly in this assembly under the
`Lakona.Game.Server.Hotfix.Abstractions` namespace, so the host and collectible
Hotfix load contexts share one framework type identity. Generated App projects
reference this package and Hotfix projects inherit the authoring surface through
their App project reference. This package also carries the compiler extension
and required build properties; there is no separate Hotfix abstractions or
generator package to install or version.

## Install

```powershell
dotnet add package Lakona.Game.Server
dotnet add package Lakona.Rpc.Transport.WebSocket
dotnet add package Lakona.Rpc.Serializer.MemoryPack
dotnet add package Microsoft.Extensions.Logging.Console
```

## Run A Game Server

```csharp
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;
using Microsoft.Extensions.Logging;

return await LakonaGameServer.RunAsync(args, static server => server
    .ConfigureLogging(static logging => logging.AddSimpleConsole())
    .RegisterEndpointTransport("websocket", static async (endpoint, cancellationToken) =>
        await WsConnectionAcceptor.CreateAsync(
            endpoint.Port,
            string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
            endpoint.Host,
            cancellationToken).ConfigureAwait(false))
    .RegisterEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer()));
```

`LakonaGameServer.RunAsync()` registers the default in-memory session services,
reliable push services, actor runtime, health checks, runtime validation,
hotfix loading, and RPC listeners derived from `Lakona:Endpoints[]`. Replace the
default stores when sessions or pending push records must survive process
restarts.

The application composition root owns logging providers. Replace
`AddSimpleConsole` with the provider required by the deployment; when no
provider is registered, Game.Server and its inbound and outbound RPC runtimes
remain externally silent. See
[Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md) for
Console, Serilog, NLog, client, and custom-provider integration.

Stable application dependencies use automatically discovered modules:

```csharp
using Lakona.Game.Server;
using Lakona.Game.Server.Modules;

[NodeRole("data")]
public sealed class PostgresModule : ILakonaModule
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(
            configuration.GetConnectionString("PostgreSql")!));
        services.AddSingleton<IApplicationStore, PostgresApplicationStore>();
    }

    public async Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken)
    {
        var dataSource =
            context.Services.GetRequiredService<NpgsqlDataSource>();
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

No entry-point registration is required. Lakona discovers the module, invokes
`ConfigureServices` before building the single root provider, and awaits
`StartAsync` before initial Hotfix loading, management HTTP, RPC listeners,
cluster Ready publication, or Startup Actors. Failed startup rolls back earlier
modules. Shutdown marks the process NotReady, stops framework consumers, stops
modules in reverse order, and then disposes the provider.

See the repository's
[Application Modules](../../docs/application-modules.md) authority for
discovery, ownership, and failure rules.

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
        "ReliablePush": true,
        "ConnectionLimits": {
          "MaxActiveConnections": 10000,
          "MaxPendingHandshakes": 1000,
          "HandshakeTimeout": "00:00:10"
        },
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Sessions": {
      "ResumeWindowSeconds": 60
    }
  }
}
```

Transport and serializer packages are explicit application dependencies.
`Program.cs` registers only the implementation names accepted by this server;
the framework matches endpoint configuration to those registrations and still
owns listener and generated-service lifecycles. Additional transports can use
the same registration seam without changing `Lakona.Game.Server`.

## Application HTTP

Application HTTP listeners are configured separately from client RPC
endpoints:

```json
{
  "Lakona": {
    "Http": {
      "Listeners": [
        {
          "Id": "payments",
          "Host": "0.0.0.0",
          "Port": 21001,
          "Services": [ "payment-webhooks" ],
          "MaximumBodyBytes": 262144,
          "RequestTimeoutSeconds": 15
        }
      ]
    }
  }
}
```

Declare the route and implement product logic together in `Server.Hotfix`:

```csharp
[LakonaHttpService("payment-webhooks")]
public sealed class PaymentWebhookService
{
    [LakonaHttpEndpoint("POST", "/payments/notify")]
    public ValueTask<LakonaHttpResponse> NotifyAsync(LakonaHttpCall call)
    {
        // Verify exact call.Request.RawBody and route state through call.Actors.
        return new(LakonaHttpResponse.Text("accepted"));
    }
}
```

The initial Hotfix generation exposes the service only on listeners whose
`Services` contains its name and freezes the process-local route manifest.
Services not selected by any listener on this node are not constructed, so a
split-role node does not need another node's application dependencies.
Later generations bind cached typed handlers to host-assigned endpoint slots;
application code does not maintain numeric HTTP method ids. Every admitted
request is pinned to one Hotfix generation, and Hotfix receives a bounded
request snapshot detached from `HttpContext`. Request deadlines are
cooperative, so handlers must observe `LakonaHttpCall.CancellationToken`. See
[Application HTTP](../../docs/http.md) for listener isolation, admission, and
reload semantics.

`Lakona.Game.Server` owns the node-to-node TCP transport and MemoryPack
serializer. When `Lakona:Cluster` is omitted, the server derives the default
one-node endpoint without composition-root cluster adapters. Keep client-facing
serializer names under `Lakona:Endpoints[]:Serializer`. Cluster nodes complete
the framework-owned [cluster protocol negotiation](../../docs/cluster.md#cluster-rpc-composition)
before RPC payload decoding.

Every process follows the same Joining-to-Active lifecycle through a Membership
Table. The default in-memory provider is for one local process. Multi-process
deployments select the PostgreSQL provider and share the named membership
connection string. One Membership database or schema belongs to one Lakona
environment; there is no logical cluster or service namespace. There is no
peer list or game-server leader election; PostgreSQL serializes
compare-and-swap membership changes.

Before starting a PostgreSQL-backed cluster, apply the package's single
`database/postgresql/membership.sql` file with a deployment account. It is
transactional, repeatable, and owns all future Membership schema changes.
Runtime node credentials need only `SELECT`, `INSERT`, `UPDATE`, and `DELETE`
on the Membership tables; nodes never execute DDL. Stop every node and apply
the latest file before an upgrade. Missing or incompatible schema fails startup
immediately instead of being created or altered by the application process.
See [Cluster Membership Table](../../docs/cluster.md#membership-table) for the
deployment sequence and PostgreSQL grants.

Reliable push is off unless an endpoint explicitly sets `ReliablePush: true`.
The endpoint policy is fixed for the lifetime of a Game Session and is sent to
the client during handshake. `Lakona:Sessions:ResumeWindowSeconds` is the
single retention window for disconnected Game Sessions and their unacknowledged
push records; it defaults to 60 seconds. The built-in stores are process-local,
so resume targets the same gateway and does not provide distributed redirect.

`AddLakonaGameServer` uses a Membership-driven distributed Actor Directory.
Consecutive Membership views hand changed hash ranges directly to their new
owners; skipped or interrupted handoffs recover from surviving activation
registries. Ordinary calls cache the exact owner reference and activation id.
Actor locations are not stored in PostgreSQL.

## Observability

Lakona emits logs, metrics, and traces through standard .NET diagnostics:
`ILogger`, `Meter`, and `ActivitySource`.

Lakona does not own an exporter, collector, Prometheus endpoint, or private
diagnostics protocol. Configure the OpenTelemetry SDK and exporters in the
application and subscribe to the names in `LakonaGameServerTelemetry`.
Liveness and readiness remain HTTP orchestration probes at
`/_lakona/health/live` and `/_lakona/health/ready`.

For a task-oriented guide, see
[Observability](../../docs/observability.md).

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
public sealed partial class RoomBehavior
{
    public ValueTask<JoinRoomReply> JoinAsync(
        RoomActor room,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        room.JoinedPlayers.Add(request.PlayerId);

        return new ValueTask<JoinRoomReply>(
            new JoinRoomReply
            {
                PlayerCount = room.JoinedPlayers.Count
            });
    }
}

var actors = provider.GetRequiredService<ActorAccess>();
var roomId = new RoomId("alpha");
var request = new JoinRoomRequest { PlayerId = 10001 };

var routed = await actors.Route<RoomActor>(roomId).CallAsync(
    static behavior => behavior.JoinAsync,
    request,
    cancellationToken);
var localOnly = await actors.Local<RoomActor>(roomId).CallAsync(
    static behavior => behavior.JoinAsync,
    request,
    cancellationToken);
```

Public methods on `RoomBehavior` declare the generated actor ref call surface
and own the implementation that runs inside the actor turn.

Annotate a remotely callable method with `[ActorMethod("stable-name")]` when
its wire identity must survive C# method renames. Mark public helpers that are
only used by lifecycle or composition code with `[ActorIgnore]`; they are not
generated, shape-validated as remote methods, or available to dispatch. The
two attributes cannot be combined on one method.

Generator support emits one `ActorAccess` root with constrained
`Local<TActor>(id)` and `Route<TActor>(id)` selectors for `Actor<TKey>` classes.
Generated selectors expose generic
`CallAsync(static behavior => behavior.MethodAsync, request, cancellationToken)` for request/reply
calls and `PostAsync(static behavior => behavior.MethodAsync, request, cancellationToken)` for
fire-and-forget dispatch after placement is explicit.

Replicated Actor placement uses committed membership and activation records.
Transport failures surface through the remote Actor status model; explicit
caller cancellation remains an `OperationCanceledException`.

## Advanced Local Actor Runtime

`IActorRuntime` remains public for generated code, framework-owned boundary
services, tests, diagnostics, and rare node-local escape hatches. It is
process-local: it does not resolve actor directory placement and it does not
route to another node. Business code should prefer generated selectors so local
versus distributed actor intent stays visible.

Use `TryTell` only when a framework boundary must fail fast on local mailbox
pressure. Use generated `Place` selectors for lifecycle management and
`IActorRuntime` mailbox metrics or state queries for diagnostics rather than
ordinary gameplay calls.

## Sessions And Push

`ILakonaGameServer` is the high-level entry point for game sessions, connection
binding, and session lifecycle. Publish callback intent through
`IClientNotifications`; reliable push sequencing, replay, and acknowledgements
are framework protocol details.

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Server;

public sealed class MatchPushService
{
    private readonly ILakonaGameServer _server;
    private readonly IClientNotifications _notifications;

    public MatchPushService(
        ILakonaGameServer server,
        IClientNotifications notifications)
    {
        _server = server;
        _notifications = notifications;
    }

    public ValueTask<GameSessionKey> LoginAsync(
        string playerId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        return _server.StartSessionAsync(playerId, connectionId, cancellationToken);
    }

    public ClientNotificationStatus PublishMatched(
        GameSessionKey session,
        MatchmakingStatusUpdate update)
    {
        return _notifications
            .ForSession<IPlayerCallback>(session)
            .OnMatchmakingStatus(update);
    }
}
```

The synchronous return status describes framework admission. `Accepted` means
Lakona now owns a bounded, per-session FIFO delivery attempt; it does not wait
for the network send. `Backpressure` means the session queue is full and the
notification was not accepted.

Reconnect recovery is framework-owned: generated clients present an opaque,
endpoint-scoped ticket during the game handshake. Business code does not call a
raw resume API or validate that credential. Product authentication and
authoritative player-state policy remain ordinary business operations after the
framework handshake. Lakona does not define account models, room rules,
matchmaking policy, persistence schema, or gameplay DTOs.

## Optional Runtime Capabilities

- Runtime validation: expose `/_lakona/health/live` and
  `/_lakona/health/ready` through `Lakona:Management:Http`, with route policy under `Lakona:Health`.
- Cluster notifications: use `IClientNotifications` from business nodes; the
  framework sends serializable callback commands to the gateway that owns the
  session.
- Startup service groups: register `RegisterStartup<TActor,TKey>(selector)` in
  a hotfix startup method marked `[HotfixConfigureActors]`; every node whose
  roles include the Actor type's `[NodeRole]` starts one ready replica.
- Hotfix timers: use `LakonaTimer.CreateOnceTimerAsync(static (Timer callbacks) => callbacks.Method, ...)` or
  `LakonaTimer.CreatePeriodicTimerAsync(static (Timer callbacks) => callbacks.Method, ...)` from `[ActorStart]`,
  store the returned `TimerId` in stable actor state, and call
  `LakonaTimer.DestroyTimerAsync(timerId, call.CleanupCancellationToken)` from
  `[ActorStop]`.

## Actor Runtime Configuration

```csharp
builder.Services.AddLakonaGameServerActors(options =>
{
    options.MailboxCapacity = 4096;
    options.SlowMessageThreshold = TimeSpan.FromSeconds(1);
    options.CallTimeout = TimeSpan.FromSeconds(30);
    options.DeactivationTimeout = TimeSpan.FromSeconds(30);
});
```

Actor ids are application-owned strings. Pick stable names such as
`player/alice`, `room/alpha`, or `match/2026-06-17-001` when other services need
to address the same actor.
