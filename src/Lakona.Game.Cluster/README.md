# Lakona.Game.Cluster

`Lakona.Game.Cluster` contains optional explicit cluster routing contracts for Lakona.Game.

This package is intentionally small. It defines node identity, node-directory abstractions, route identity, generation-aware route locations, message envelopes, explicit actor route envelopes, route directory abstractions, router abstractions, a loopback messenger, and in-memory implementations for tests or local single-process validation.

Diagnostics are exposed through the `Lakona.Game.Cluster` `Meter` and `ActivitySource`. Metrics use low-cardinality tags such as stage, status, delivery, and message kind.

It does not provide a production network adapter, Redis-specific state, external platform discovery bindings, remote actor proxies, actor migration, or durable route state.

Actor route helpers produce route keys from application-chosen actor ids only. They do not encode node ids, endpoints, execution lanes, or Lakona.Actor scheduler internals.

Route locations include a route generation, node epoch, endpoint, lease expiration, and metadata. In-memory registration rejects stale generations and older node epochs, and lease refresh requires the caller to present the matching route owner. This keeps restarted nodes and moved route owners from accidentally reviving old ownership.

The selected first production adapter direction is the separate `Lakona.Game.Cluster.Rpc` package. The core package remains transport-neutral.

## Cluster Configuration

Cluster configuration uses static bootstrap settings plus a dynamic node directory. Static settings tell a node its own identity, which services to load, which endpoints to advertise, and how to reach a node-directory service. The live cluster view comes from the node directory.

In Lakona.Game cluster terminology, a node is one .NET server process. Machine, process, and node are treated as the same deployment unit. Services are configured inside a node. A development node can host every service in one process, while a production deployment can split the same services across several nodes without changing route or messaging code.

### All-In-One Development Node

```json
{
  "Cluster": {
    "Name": "local-dev",
    "NodeId": "dev-1",
    "AdvertisedEndpoints": {
      "cluster": "tcp://127.0.0.1:21000",
      "client": "ws://127.0.0.1:20000/ws"
    },
    "Bootstrap": {
      "NodeDirectoryEndpoints": [
        "tcp://127.0.0.1:21000"
      ]
    },
    "NodeDirectory": {
      "Enabled": true,
      "Storage": {
        "Mode": "InMemory"
      }
    },
    "Services": [
      { "Kind": "node-directory", "Name": "node-directory" },
      { "Kind": "route-directory", "Name": "route-directory" },
      { "Kind": "gateway", "Name": "gateway" },
      { "Kind": "lobby", "Name": "lobby" },
      { "Kind": "match", "Name": "match" },
      { "Kind": "room", "Name": "room" },
      { "Kind": "chat", "Name": "chat" }
    ],
    "Lease": {
      "Seconds": 30,
      "HeartbeatSeconds": 10
    }
  }
}
```

This layout is for local development and smoke tests. The node-directory service runs in the same node as the game services, and its storage can be in-memory.

### Split Production Nodes

```json
{
  "Cluster": {
    "Name": "prod-cn-1",
    "NodeId": "control-1",
    "AdvertisedEndpoints": {
      "cluster": "tcp://10.0.0.10:21000"
    },
    "Bootstrap": {
      "NodeDirectoryEndpoints": [
        "tcp://10.0.0.10:21000",
        "tcp://10.0.0.11:21000"
      ]
    },
    "NodeDirectory": {
      "Enabled": true,
      "Storage": {
        "Mode": "Persistent",
        "Provider": "postgres",
        "ConnectionStringName": "ClusterDirectory"
      }
    },
    "Services": [
      { "Kind": "node-directory", "Name": "node-directory" },
      { "Kind": "route-directory", "Name": "route-directory" }
    ],
    "Lease": {
      "Seconds": 30,
      "HeartbeatSeconds": 10
    }
  }
}
```

```json
{
  "Cluster": {
    "Name": "prod-cn-1",
    "NodeId": "room-1",
    "AdvertisedEndpoints": {
      "cluster": "tcp://10.0.1.20:21000"
    },
    "Bootstrap": {
      "NodeDirectoryEndpoints": [
        "tcp://10.0.0.10:21000",
        "tcp://10.0.0.11:21000"
      ]
    },
    "Services": [
      {
        "Kind": "room",
        "Name": "room",
        "Metadata": {
          "MaxRooms": "1000"
        }
      }
    ],
    "Lease": {
      "Seconds": 30,
      "HeartbeatSeconds": 10
    }
  }
}
```

The node-directory service is a normal node-local service. Production deployments may place it on a control node, co-locate it with other services, or run multiple configured directory nodes once the selected persistent store and concurrency policy support that topology.

### Node Directory Storage

The core package includes transport-neutral node-directory contracts and the in-memory implementation:

- `InMemory`: tests, local validation, and all-in-one development.
- `Persistent`: production-oriented deployments through `Lakona.Game.Cluster.Sql` or project-owned adapters.

Persistent storage is required so `NodeEpoch` allocation does not roll back after a directory restart and active leases can be recovered or expired consistently. It is live membership metadata, not a business event log and not durable route ownership.

The core cluster package does not depend on a persistent provider. Concrete persistent providers such as SQL databases, Redis, Consul, etcd, or Kubernetes API integration should be adapters selected by project configuration, not assumptions baked into route or messaging APIs.

## Route Key Conventions

`ClusterActorRouteKeys` provides standard route key helpers:

- `ForActor("player/alice")` → route key `"actor:player/alice"` — actor-targeted messages
- `ForReply(nodeId)` → route key `"actor-reply:<nodeId>"` — reply messages for `RemoteActorGateway`

These are conventions, not protocol requirements. Projects can define their own route key schemes.
