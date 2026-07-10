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

## Startup Order

Server startup follows this high-level order:

1. Bind `Lakona` runtime configuration.
2. Register generated actor selectors and hotfix service bindings.
3. Configure RPC endpoints and cluster endpoint serializers.
4. Start actor startup declarations selected by `Lakona:StartupActors`.
5. Register node endpoints, actor hosts, and labels in the node directory.
6. Start RPC listeners.
7. After every enabled framework listener has bound and all hosted startup work
   has completed, log `Lakona server started successfully` with the node id.

On shutdown, RPC listeners stop first, then node membership is marked dead, and
actor lifecycle cleanup runs through the actor runtime.
