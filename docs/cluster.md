# Cluster

Lakona cluster support is explicit infrastructure for multi-process game
servers. It provides node membership, endpoint advertisement, actor-host
advertisement, route ownership, remote actor dispatch, and session notification
relay. It does not define a second application component model.

## Terms

| Term | Meaning |
| --- | --- |
| Node | One server process with a stable `Lakona:Node:Id`. |
| Endpoint | A transport address advertised by the node. |
| Actor host | An actor kind this node is allowed to create locally. |
| Startup service group | One ready replica per capable node, selected through an application-owned keyed policy. |
| Label | Low-cardinality node metadata used for framework discovery. |
| Route | A mapping from route key to the node that currently owns it. |

## Configuration

Cluster configuration lives under the application `Lakona` root:

```json
{
  "Lakona": {
    "Node": {
      "Id": "data-1"
    },
    "ActorHosts": [ "user", "matchmaking", "leaderboard" ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "Serializer": "memorypack",
      "Seeds": [ "tcp://10.0.0.1:21001" ],
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

`ActorHosts` controls which actor kinds a node may create. A Startup declaration
in hotfix code intersects with this capability list: each capable node creates
one replica and advertises it only while ready. Placement, routing, and Startup
selection policy belong in code, not per-node configuration.

Gateway-only nodes normally use an empty actor-host list while still
exposing client RPC endpoints:

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
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "AdvertisedHost": "gateway-1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Serializer": "memorypack",
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

Actor-directory wiring follows `Lakona:Cluster:Seeds`. The node whose cluster
endpoint matches the first configured seed owns the ephemeral in-memory actor
directory; later seeds do not become alternate directory owners. Remote nodes
use the first seed for actor ownership operations without any separate
actor-directory configuration. Restarting that seed may clear ownership
records. Persistent actor ownership, replication, and directory failover are
not provided by this topology.

## Node Directory

The node directory stores live membership metadata:

- cluster name
- node id and node epoch
- readiness state
- advertised endpoints
- advertised actor hosts
- low-cardinality labels
- lease expiration and update time

SQL storage is provided by `Lakona.Game.Cluster.Sql`. Projects may replace the
directory with another adapter if they preserve the same membership semantics.

Node-directory persistence is separate from actor ownership. Nodes advertise
their configured actor hosts, but no node advertises or discovers an
actor-directory label; the configured seed is the only actor-directory target.

`INodeDirectory` is the lookup boundary for traffic whose destination
`NodeId` is already known. `IClusterNodeSender` uses it to resolve the node's
advertised cluster endpoint and send framework control messages or replies
directly to that node.

The in-memory node and route directories use conditional concurrent updates.
Reads and heartbeats do not wait for a directory-wide lock; query and cleanup
enumerate a point-in-time-safe view and only replace or remove the exact record
they observed. A concurrent lease refresh, route generation, node epoch, or
state update therefore wins over stale cleanup work.

Cluster RPC clients are cached by node, node epoch, and endpoint. Warm cache
hits are lock-free, concurrent misses for the same identity share one connect
task, and a superseded or losing client is disposed when the newer identity is
published.

## Session Notification Relay

The node that owns a client-session route also owns reliable-push state for
that session id. Notification publication resolves route ownership
before sequence allocation:

```text
local producer -> local route owner -> owner outbox -> callback
remote producer -> cluster intent -> route owner outbox -> callback
```

Remote producers relay an unsequenced notification intent. Cluster notification
commands do not carry authoritative reliable-push metadata; the route owner
creates metadata from the record it adds to its own outbox. This keeps sequence
allocation, pending records, acknowledgements, and replay on the same node.

A missing or stale session route ends the background delivery attempt without
creating an outbox on the producer. Business publication has already returned
`Accepted` after local framework admission, so the route outcome is emitted
through framework diagnostics. Built-in outboxes are process-local and are not
transferred when an owner fails or session route ownership moves.

## Actor Routing

Generated actor selectors express placement intent:

```csharp
await actors.Route<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, ct);
await actors.Local<RoomActor>(roomId).PostAsync(static behavior => behavior.RunTickAsync, request, ct);
await actors.Startup<MatchmakingActor>(queueId).CallAsync(static behavior => behavior.EnqueueAsync, request, ct);
```

`Route(id)` may use registered routing policy to select a node. Typical
policies include stable hash, random ready node, fixed node, or product-owned
logic. The framework only requires the policy to choose from eligible ready
nodes and return a valid target.

`Startup(key)` is different from actor placement. `TKey` is routing affinity,
not the physical actor id: the registered selector receives the key and the
current ready candidates, and its strategy is fixed at registration time.
Physical ids such as `matchmaking/@startup/data-1` remain framework-internal.
On a definitely-not-executed attempt the same key may be reselected against the
remaining replicas. Ambiguous failures are never retried. Replica state is not
replicated; after failover an in-memory queue may be empty by design.

Business actor routes are ownership mappings resolved through
`IRouteDirectory` by `IClusterRouter`. They are distinct from node-directed
framework traffic: control messages and replies addressed to a known `NodeId`
go through `IClusterNodeSender` and `INodeDirectory`. The
`ClusterActorRouteKeys.ForReply(nodeId)` value on a reply message (currently
`actor-reply:<node-id>`) is only the destination node's local handler key; it
is never registered as a cluster route in `IRouteDirectory`.

Actor ownership lookup, registration, and removal use the Actor Directory on
the seed. Transport failures, serialization or deserialization failures, and
seed unavailability surface as `ActorDirectoryUnavailableException`; explicit
caller cancellation remains cancellation and is not wrapped.

## Startup Order

Server startup follows this high-level order:

1. Bind `Lakona` runtime configuration.
2. Register generated actor selectors and hotfix service bindings.
3. Configure RPC endpoints and cluster endpoint serializers.
4. Start one replica for every Startup declaration allowed by `ActorHosts`.
5. Register node endpoints, actor hosts, and ready Startup descriptors.
6. Start RPC listeners.
7. After every enabled framework listener has bound and all hosted startup work
   has completed, log `Lakona server started successfully` with the node id.

On shutdown, RPC listeners stop first, then node membership is marked dead, and
actor lifecycle cleanup runs through the actor runtime.
