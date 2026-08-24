# Configuration

Lakona runtime configuration is bound from the `Lakona` root. The runtime model
is node, endpoints, actor hosting, cluster infrastructure,
heartbeat, hotfix, and observability.

Application HTTP and Management HTTP are both hosted by the root ASP.NET Core
application. Application listeners are explicit and never alter the existing
client RPC endpoint configuration.

## Root Shape

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "ActorHosts": [ "user", "matchmaking", "leaderboard", "room" ],
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
    "Cluster": {
      "Id": "arena-dev",
      "Endpoint": "tcp://127.0.0.1:21001",
      "Membership": {
        "Provider": "Memory"
      }
    },
    "Notifications": {
      "BatchWindowMilliseconds": 10,
      "MaximumBatchSize": 256,
      "MaximumBatchBytes": 262144,
      "MaximumPendingPerSession": 256,
      "MaximumPendingPerProcess": 65536
    },
    "Timers": {
      "MaxActiveTimers": 65536
    }
  }
}
```

## Environment Variables

.NET configuration supports arrays with numeric keys and JSON string values.
Lakona accepts both forms for arrays:

```bash
Lakona__ActorHosts__0=user
Lakona__ActorHosts__1=matchmaking
```

```bash
Lakona__ActorHosts='["user","matchmaking"]'
```

Use JSON string arrays in Docker Compose when compact values are easier to read:

```yaml
environment:
  Lakona__Node__Id: data-1
  Lakona__ActorHosts: '["user","matchmaking","leaderboard"]'
  Lakona__Cluster__Endpoint: tcp://0.0.0.0:21001
```

## Endpoints

`Lakona:Endpoints[]` declares client-facing RPC listeners. Each endpoint owns
its transport, serializer, bind host, advertised host, port, path, and exposed
RPC services. Endpoint serializers are separate from cluster RPC composition.
Every configured transport and serializer name must be registered by the host
composition root. Generated projects register only the implementations selected
at project creation, so changing a name also requires adding the corresponding
package reference and startup registration.

The endpoint's effect on Game Session recovery is defined in
[Session Lifecycle](./session.md#reliable-push-and-resume).

`ReliablePush` is an explicit endpoint opt-in. It defaults to `false`; only
`"ReliablePush": true` retains unacknowledged callback commands for replay
across RPC connections. Transport choice does not imply this policy.

`ConnectionLimits` bounds the RPC Sessions owned by that endpoint:

- `MaxActiveConnections` is the hard limit for all active RPC connections and
  defaults to `10000`. The RPC host rejects and closes a newly accepted
  transport before creating a Session when this budget is full.
- `MaxPendingHandshakes` is the subset allowed to remain connected before the
  Game Handshake completes and defaults to `1000`. It must be positive and no
  greater than `MaxActiveConnections`.
- `HandshakeTimeout` is the exact interval from RPC Session admission to a
  successful Game Handshake and defaults to `00:00:10`. Expiry cancels the RPC
  Session, releases the pending-handshake slot immediately, and releases the
  active-connection slot after Session cleanup.

Completing the Game Handshake releases only the pending-handshake budget. The
connection keeps its active-connection slot until its RPC Session ends. These
limits are per endpoint, so control WebSocket and realtime KCP listeners can
use different capacities. `HandshakeTimeout` is not a maximum lifetime for an
established connection, and `MaxPendingAcceptedConnections` is a separate RPC
acceptor queue bound rather than an active-connection limit.

## Application HTTP

Application HTTP adds a separate `Lakona:Http:Listeners[]` collection without
renaming or changing `Lakona:Endpoints[]`. Each listener owns an
operator-facing id, bind address, request limits, and the generated HTTP service
contracts exposed on that socket.

For example, one process may expose an internal operations service and a public
payment-notification service independently:

```json
{
  "Lakona": {
    "Http": {
      "Listeners": [
        {
          "Id": "operations",
          "Host": "10.0.0.10",
          "Port": 21000,
          "Services": [ "operations" ],
          "MaximumBodyBytes": 1048576,
          "RequestTimeoutSeconds": 30
        },
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

Application HTTP listeners are business ingress. They use the distributed-work
admission gate and dispatch all product behavior through the current Hotfix
generation. They never expose `/_lakona/**` or inherit Game Session semantics.
The bind address and deployment network determine exposure; Lakona does not
attach a passive public/internal classification to a listener.
The complete contract is documented in
[Application HTTP](./http.md).

## Session Resume

`Lakona:Sessions:ResumeWindowSeconds` controls how long a disconnected Game
Session may resume and defaults to 60 seconds. The server captures an exact
deadline at disconnect and sends the effective window in `GameServerHello`.
The same window bounds pending reliable callbacks, client-session route
availability, and retained terminal recovery outcomes. Cleanup interval only
controls physical scanning and cannot extend an exact recovery deadline.

Session cleanup is always active in the default game-server composition so
expired disconnected and retained-terminal records cannot accumulate for the
process lifetime. `Lakona:Sessions:Cleanup:IntervalSeconds` controls the scan
interval and defaults to 30 seconds. There is no cleanup enable/disable switch.

Automatic Game Session recovery has no separate switch. Once a Game Session
exists, the generated client retries the same endpoint within this window using
a framework-only opaque ticket. `ReliablePush` only selects whether callback
commands produced during the interruption are retained and replayed.

`Lakona:ReliablePush:MaxPendingPerSession` defaults to 256. Reaching it marks
the session `StateRefreshRequired`; Lakona never drops an old prefix and claims
that replay is complete.

## Hotfix Development Reload

`Lakona:Hotfix:DebugWatcher` controls the local-development watcher that
rebuilds and reloads the current Hotfix output through `reload.signal`. Its
runtime default is `Off`; generated local projects explicitly set it to `On`.
Production deployments should leave it off and use the installation and
activation flow defined by
[Packaging and Deployment](./deployment.md#hotfix-installation-and-activation).

## Cluster

`Lakona:Cluster` declares node-to-node infrastructure:

- `Id`: stable deployment name used to select rows in the Membership Table;
  default `default`.
- `Endpoint`: local advertised TCP endpoint used by other game-server nodes.
- `SendTimeoutMilliseconds`: positive finite deadline for node-to-node RPC;
  default `3000`.
- `Membership`: shared-membership provider and failure-detection settings.

`Lakona:Node:Id` must identify one logical process slot and must be unique among
simultaneously running nodes. A restarted process gets a new process
incarnation and a higher generation allocated by the shared table. That
generation atomically replaces and fences the old row with the same stable id
without relying on machine-clock order.

The default Membership provider is `Memory`, which is intentionally limited to
one process. Every multi-process deployment must use `Postgres` and give every
node the same `Cluster.Id` and connection string:

```json
{
  "ConnectionStrings": {
    "LakonaClusterPostgres": "Host=postgres;Database=lakona;Username=lakona;Password=change-me"
  },
  "Lakona": {
    "Cluster": {
      "Id": "arena-production",
      "Endpoint": "tcp://server-1:21001",
      "Membership": {
        "Provider": "Postgres",
        "ConnectionStringName": "LakonaClusterPostgres"
      }
    }
  }
}
```

Membership settings and defaults:

| Setting | Default | Meaning |
| --- | ---: | --- |
| `Provider` | `Memory` | `Memory` for one local process; `Postgres` for a cluster. |
| `ConnectionStringName` | `LakonaClusterPostgres` | Name under `ConnectionStrings` used by the PostgreSQL provider. |
| `TableRefreshSeconds` | `5` | Interval for reading a newer committed table view. |
| `IAmAliveSeconds` | `30` | Table heartbeat interval and maximum time a node may keep admitting work without reaching the Membership Table. Must exceed `TableRefreshSeconds`. |
| `AllowedIAmAliveMissSeconds` | `600` | Age after which a network-unreachable Active row can be cleared during startup. |
| `DefunctEntryRetentionSeconds` | `604800` | How long Dead incarnation rows are retained for diagnosis before cleanup. |
| `DefunctEntryCleanupIntervalSeconds` | `3600` | Interval between bounded cleanup passes. |
| `DefunctEntryCleanupBatchSize` | `1000` | Maximum Dead rows removed by one cleanup pass. |
| `ProbeIntervalSeconds` | `10` | Interval between network-probe rounds. |
| `ProbeTimeoutSeconds` | `2` | Timeout for one direct or indirect probe RPC. |
| `FailedProbesBeforeSuspect` | `3` | Failed rounds before committing a suspicion vote. |
| `MonitoredNodes` | `3` | Successors each Active node monitors on the hash ring. |
| `IndirectProbes` | `2` | Other nodes asked to verify a failed direct probe. |
| `VotesForDeath` | `2` | Desired distinct votes before a member becomes Dead, capped for small clusters. |
| `SuspectVoteLifetimeSeconds` | `180` | Age after which a suspicion vote no longer counts. |

The cluster transport and serializer are framework-owned rather than
configuration choices. `Lakona.Game.Server` always uses TCP and MemoryPack for
node-to-node RPC. The URI scheme of `Lakona:Cluster:Endpoint` must therefore be
`tcp`. Nodes complete the framework-owned
[cluster protocol negotiation](./cluster.md#cluster-rpc-composition) before any
RPC payload is decoded, so incompatible package generations fail as connections
instead of corrupting cluster messages.

Formation, membership, fencing, and routing behavior belong to
[Cluster](./cluster.md#formation-admission-and-identity-conflicts).

The Membership Table stores framework metadata only. Application databases
remain under application-owned configuration roots and do not have to share a
connection with cluster membership.

## Notifications

`Lakona:Notifications` configures synchronous producer admission and remote
gateway batching:

- `BatchWindowMilliseconds`: maximum remote batching wait; default `10`, and
  `0` flushes immediately.
- `MaximumBatchSize`: maximum commands in one remote batch; default `256`.
- `MaximumBatchBytes`: approximate serialized byte budget per batch; default
  `262144`.
- `MaximumPendingPerSession`: producer queue budget for one session; default
  `256`.
- `MaximumPendingPerProcess`: total producer queue budget; default `65536`.

Exhausting either pending-command budget returns `Backpressure`. Batching never
coalesces or overwrites accepted business notifications.

## Actor Hosting

`Lakona:ActorHosts` is the list of actor kinds this node may create locally.
Startup service groups are declared in code with
`RegisterStartup<TActor,TKey>(selector)`. Every node whose `ActorHosts` contains
that actor kind starts one local replica and advertises it only after its
`[ActorStart]` lifecycle succeeds. Configuration does not declare Startup
Actor groups.

Actor placement and Startup selection policy belong in code. Per-node
configuration only declares which actor kinds the node is capable of hosting.

## Timers

`Lakona:Timers:MaxActiveTimers` is the process-wide budget for framework-owned
Hotfix timers and defaults to `65536`. Creating another timer when the budget is
full fails immediately; the framework does not queue, retry, or evict an
existing business timer. Destroyed heap entries are compacted amortized after
they materially outnumber live registrations, without a background cleanup
loop.

The `Lakona.Game.Timer` meter reports `lakona.game.timer.active`,
`lakona.game.timer.heap.entries`, `lakona.game.timer.heap.stale`, and
`lakona.game.timer.capacity.rejected` without Timer ids or other high-cardinality
tags.

## Logging

Lakona does not define a private observability configuration section.
Provider ownership, runtime integration points, front-end and back-end setup,
and replacement examples for Console, Serilog, NLog, and custom providers are
defined by [Logging](./logging.md).

## Validation

Readiness validation checks node identity, endpoint connection limits, endpoints, cluster endpoint shape,
actor host names, hotfix source, heartbeat policy, and management exposure.
The shared management HTTP listener is configured independently from the routes
it serves:

```json
{
  "Lakona": {
    "Management": {
      "Http": {
        "Host": "127.0.0.1",
        "Port": 20080
      },
      "Admin": {
        "Enabled": true,
        "RequireLoopback": true
      }
    },
    "Health": {
      "Enabled": true,
      "RequireLoopback": true
    }
  }
}
```

`Lakona:Management:Http` owns the shared listener address. `Lakona:Health` and
`Lakona:Management:Admin` independently own route enablement and access
policy. Request the ready endpoint from a live process:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

The framework emits `Lakona server started successfully. NodeId={NodeId}. LakonaBuildTag={LakonaBuildTag}.` only
after Startup replicas and lifecycle callbacks complete, cluster registration
succeeds, and every enabled RPC, cluster, and management listener has bound
successfully. Health and admin routes share that listener rather than
opening separate ports.

`Lakona:Health:ClusterDiagnosticsEnabled` defaults to `false`. When explicitly
enabled it adds `GET /_lakona/health/cluster` to the existing health listener;
it remains subject to `RequireLoopback`. Its `cluster`, `view`, and member
`state` values are the local committed membership snapshot only. HTTP 200 does
not prove current quorum, distributed admission, or application readiness.

The validation boundary should report configuration problems before runtime
listeners are opened. The runtime readiness contract belongs to
[Guardrails](./guardrails.md).
