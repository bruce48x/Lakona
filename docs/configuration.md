# Configuration

Lakona runtime configuration is bound from the `Lakona` root. The runtime model
is node, endpoints, actor hosting, cluster infrastructure,
heartbeat, hotfix, and observability.

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
      "Serializer": "memorypack",
      "Seeds": [ "tcp://127.0.0.1:21001" ]
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
  Lakona__Cluster__Serializer: memorypack
```

## Endpoints

`Lakona:Endpoints[]` declares client-facing RPC listeners. Each endpoint owns
its transport, serializer, bind host, advertised host, port, path, and exposed
RPC services. Endpoint serializers are separate from the cluster serializer.

`ReliablePush` is an explicit endpoint opt-in. It defaults to `false`; only
`"ReliablePush": true` retains unacknowledged callback commands for replay
across RPC connections. Transport choice does not imply this policy.

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
- `Serializer`: `json` or `memorypack`.
- `Seeds`: endpoints used to reach shared directory services.
- `Directory`: optional SQL directory provider configuration.

Shared directory storage belongs under `Lakona:Cluster:Directory`. Application
databases belong under application-owned configuration roots.

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
