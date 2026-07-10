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
| Startup actor | A named actor that should be created during node startup. |
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
    "StartupActors": [ "matchmaking", "leaderboard" ],
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

`ActorHosts` controls which actor kinds a node may create. `StartupActors`
controls named actors created at startup. Placement and routing policy belongs
in code, through generated actor selectors and registered route policies, not
in per-node placement configuration.

Gateway-only nodes normally use empty actor-host and startup lists while still
exposing client RPC endpoints:

```json
{
  "Lakona": {
    "Node": {
      "Id": "gateway-1"
    },
    "ActorHosts": [],
    "StartupActors": [],
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
endpoint is the configured seed owns the ephemeral in-memory actor directory;
remote nodes use that seed for actor ownership operations without any separate
actor-directory configuration. Restarting the seed may clear ownership
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

## Session Notification Relay

The node that owns a client-session route also owns reliable-push state for
that session generation. Notification publication resolves route ownership
before sequence allocation:

```text
local producer -> local route owner -> owner outbox -> callback
remote producer -> cluster intent -> route owner outbox -> callback
```

Remote producers relay an unsequenced notification intent. Cluster notification
commands do not carry authoritative reliable-push metadata; the route owner
creates metadata from the record it adds to its own outbox. This keeps sequence
allocation, pending records, acknowledgements, and replay on the same node.

A missing or stale session route returns `RouteNotFound` without creating an
outbox on the producer. Built-in outboxes are process-local and are not
transferred when an owner fails or a session generation moves.

## Actor Routing

Generated actor selectors express placement intent:

```csharp
await rooms.Route(roomId).CallAsync(RoomBehavior.JoinAsync, request, ct);
await rooms.Local(roomId).PostAsync(RoomBehavior.RunTickAsync, request, ct);
```

`Route(id)` may use registered routing policy to select a node. Typical
policies include stable hash, random ready node, fixed node, or product-owned
logic. The framework only requires the policy to choose from eligible ready
nodes and return a valid target.

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
4. Start actor startup declarations selected by `Lakona:StartupActors`.
5. Register node endpoints, actor hosts, and labels in the node directory.
6. Start RPC listeners.

On shutdown, RPC listeners stop first, then node membership is marked dead, and
actor lifecycle cleanup runs through the actor runtime.
