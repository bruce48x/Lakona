# Lakona.Game.Cluster

`Lakona.Game.Cluster` contains optional explicit cluster routing contracts for
Lakona.Game.

This package is intentionally small. It defines node identity, node-directory
abstractions, route identity, generation-aware route locations, message
envelopes, route directory abstractions, router
abstractions, a loopback messenger, and in-memory implementations for tests or
local single-process validation.

Diagnostics are exposed through the `Lakona.Game.Cluster` `Meter` and
`ActivitySource`. Metrics use low-cardinality tags such as stage, status,
delivery, and message kind.

It does not provide a production network adapter, Redis-specific state,
external platform discovery bindings, remote actor proxies, actor migration, or
durable route state. RPC-specific clients, binders, and TCP transport behavior
live in the separate `Lakona.Game.Cluster.Rpc` package.

Actor route helpers produce route keys from application-chosen actor ids only.
They do not encode node ids, endpoints, execution lanes, or local actor-kernel
scheduler internals.

Route locations include a route generation, node epoch, endpoint, lease
expiration, and metadata. In-memory registration rejects stale generations and
older node epochs, and lease refresh requires the caller to present the
matching route owner. This keeps restarted nodes and moved route owners from
accidentally reviving old ownership.

## Cluster Configuration

Runtime configuration uses the application `Lakona` root. Static settings tell a
node its own identity, which actor kinds it may host, which client
endpoints to expose, which cluster endpoint to advertise, and which seed
endpoints can reach the cluster directory. The live cluster view comes from the
node directory.

In Lakona.Game cluster terminology, a node is one .NET server process. Machine,
process, and node are treated as the same deployment unit. Actor hosting
capability is configured inside a node. A development node can host all actor
kinds in one process, while production can split actor kinds across several
nodes without changing route or messaging code.

### All-In-One Development Node

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "ActorHosts": [ "room", "matchmaking", "leaderboard" ],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "json",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      },
      {
        "Transport": "kcp",
        "Serializer": "json",
        "Host": "127.0.0.1",
        "Port": 20001,
        "RpcServices": [ "battle" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://127.0.0.1:21000",
      "Serializer": "json",
      "Seeds": [ "tcp://127.0.0.1:21000" ],
      "RouteLeaseSeconds": 30
    }
  }
}
```

This layout is for local development and smoke tests. The node-directory and
route-directory implementations are ordinary DI services supplied by the game
server process or project configuration. Their storage can be in-memory for
local validation.

### Split Production Nodes

```json
{
  "Lakona": {
    "Node": {
      "Id": "data-1"
    },
    "ActorHosts": [ "matchmaking", "leaderboard" ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "Seeds": [ "tcp://10.0.0.1:21001" ],
      "RouteLeaseSeconds": 30,
      "Directory": {
        "Provider": "postgres",
        "ConnectionStringName": "LakonaClusterPostgres",
        "NodeTable": "lakona_cluster_nodes",
        "EnsureSchemaOnStartup": false
      }
    }
  }
}
```

```json
{
  "Lakona": {
    "Node": {
      "Id": "gateway-1"
    },
    "ActorHosts": [],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "json",
        "Host": "0.0.0.0",
        "AdvertisedHost": "gateway-1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

The data node above can provide persistent framework cluster membership through
`Lakona:Cluster:Directory` and `Lakona.Game.Cluster.Sql`. The gateway node
hosts no application actors because `ActorHosts` is empty, but it still exposes
client RPC services and a node-to-node cluster endpoint.

Startup service groups are declared in hotfix code. Every node whose
`ActorHosts` includes the actor kind starts one replica and advertises a ready
descriptor through the node directory. Replica state is node-local.

Every node that configures `Lakona:Cluster:Endpoint` must listen on that
endpoint. Framework-owned cluster endpoint hosting binds node-directory RPC,
route-directory RPC, notification relay, and remote actor dispatch when the
corresponding local services are registered in DI.

The application selects one node-to-node transport and serializer pair with
`LakonaGameServerBuilder.UseClusterRpc`. That code-level choice is separate
from endpoint-local client RPC serializer names. Cluster peers negotiate the
serializer protocol before RPC starts.

## Node Directory Storage

The core package includes transport-neutral node-directory contracts and the
in-memory implementation:

- `InMemory`: tests, local validation, and all-in-one development.
- `Persistent`: production-oriented deployments through
  `Lakona.Game.Cluster.Sql` or project-owned adapters.

Persistent storage is required so `NodeEpoch` allocation does not roll back
after a directory restart and active leases can be recovered or expired
consistently. It is live membership metadata, not a business event log and not
durable route ownership.

The core cluster package does not depend on a persistent provider. Concrete
persistent providers such as SQL databases, Redis, Consul, etcd, or Kubernetes
API integration should be adapters selected by project configuration, not
assumptions baked into route or messaging APIs.

## Route Key Conventions

`ClusterActorRouteKeys` provides standard route key helpers:

- `ForActor("player/alice")` -> route key `"actor:player/alice"` for
  actor-targeted messages.
- `ForReply(nodeId)` -> route key `"actor-reply:<nodeId>"` for reply messages
  used by `RemoteActorGateway`.

These are conventions, not protocol requirements. Projects can define their
own route key schemes.
