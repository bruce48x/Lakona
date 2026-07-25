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
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://127.0.0.1:21001",
      "BootstrapNewCluster": true,
      "Seeds": []
    },
    "Notifications": {
      "BatchWindowMilliseconds": 10,
      "MaximumBatchSize": 256,
      "MaximumBatchBytes": 262144,
      "MaximumPendingPerSession": 256,
      "MaximumPendingPerProcess": 65536
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

`ReliablePush` is an explicit endpoint opt-in. It defaults to `false`; only
`"ReliablePush": true` retains unacknowledged callback commands for replay
across RPC connections. Transport choice does not imply this policy.

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
The same window bounds pending reliable callbacks and client-session route
availability. Cleanup interval only controls scanning and cannot extend the
deadline.

Automatic Game Session recovery has no separate switch. Once a Game Session
exists, the generated client retries the same endpoint within this window using
a framework-only opaque ticket. `ReliablePush` only selects whether callback
commands produced during the interruption are retained and replayed.

`Lakona:ReliablePush:MaxPendingPerSession` defaults to 256. Reaching it marks
the session `StateRefreshRequired`; Lakona never drops an old prefix and claims
that replay is complete.

## Cluster

`Lakona:Cluster` declares node-to-node infrastructure:

- `Endpoint`: local advertised cluster endpoint.
- `BootstrapNewCluster`: explicit authorization to create a fresh in-memory
  cluster incarnation. It defaults to `false`.
- `Seeds`: unordered discovery contacts used to join an existing replicated
  cluster. Seed order does not select the leader or a directory owner.
- `Directory`: legacy compatibility-directory configuration. Replicated
  hosting does not use it for membership, actor activation, or session routes.

`BootstrapNewCluster=true` and a non-empty `Seeds` list are rejected together.
An unreachable seed never authorizes an implicit fresh bootstrap.
Joining retries discovery contacts with bounded exponential backoff for 30
seconds by default (`ClusterMembershipNodeOptions.JoinRetryWindow`). This is a
programmatic cluster-runtime option rather than application transport
configuration; exhausting it fails startup without bootstrapping another
cluster.

The cluster transport and serializer are code dependencies, not string
configuration. The server composition root selects exactly one bidirectional
transport and one serializer protocol:

```csharp
server.UseClusterRpc(
    TcpClusterRpcTransport.Default,
    MemoryPackClusterRpcSerializer.Default);
```

The URI scheme of `Lakona:Cluster:Endpoint` and every seed must match the
selected transport. Peers negotiate the serializer protocol before any RPC
payload is decoded, so mixed JSON and MemoryPack nodes fail as incompatible
connections instead of corrupting cluster messages.

Replicated framework state is intentionally process-local and does not require
shared SQL storage. Application databases belong under application-owned
configuration roots.

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
`[ActorStart]` lifecycle succeeds. `Lakona:StartupActors` has been removed and
is rejected as invalid configuration.

Actor placement and Startup selection policy belong in code. Per-node
configuration only declares which actor kinds the node is capable of hosting.

## Validation

Readiness validation checks node identity, endpoints, cluster endpoint shape,
actor host names, hotfix source, heartbeat policy, and observability settings.
The shared management HTTP listener is configured independently from the routes
it serves:

```json
{
  "Lakona": {
    "Management": {
      "Http": {
        "Host": "127.0.0.1",
        "Port": 20080
      }
    },
    "Health": {
      "Enabled": true,
      "RequireLoopback": true
    },
    "Observability": {
      "LocalAdmin": {
        "Enabled": true,
        "RequireLoopback": true
      }
    }
  }
}
```

`Lakona:Management:Http` owns the shared listener address. `Lakona:Health` and
`Lakona:Observability:LocalAdmin` independently own route enablement and access
policy. Request the ready endpoint from a live process:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

The framework emits `Lakona server started successfully. NodeId={NodeId}.` only
after Startup replicas and lifecycle callbacks complete, cluster registration
succeeds, and every enabled RPC, cluster, and management listener has bound
successfully. Health and local-admin routes share that listener rather than
opening separate ports.

The validation boundary should report configuration problems before runtime
listeners are opened.
