# Lakona.Game.Cluster.Rpc

`Lakona.Game.Cluster.Rpc` contains the Lakona.Rpc adapter layer for explicit Lakona.Game cluster node-to-node messaging, remote node-directory calls, and remote route-directory calls.

The package stays outside `Lakona.Game.Cluster` so core route contracts remain transport-neutral. It provides:

- a Lakona.Rpc method contract for sending `ClusterMessage` envelopes between nodes
- `ClusterNodeMessenger`, an `INodeMessenger` implementation backed by a Lakona.Rpc client factory
- `ClusterClientFactory`, a reusable client cache over application-provided Lakona.Rpc transports
- `IClusterTransportFactory`, the boundary where projects choose TCP, WebSocket, KCP, security, and endpoint policy
- `TcpClusterTransportFactory`, a TCP transport factory for endpoint addresses such as `tcp://127.0.0.1:20010`
- `ClusterMessageBinder`, a server-side binder that dispatches inbound cluster messages into an `IClusterMessageHandler`
- `NodeDirectoryClient`, an `INodeDirectory` client backed by Lakona.Rpc calls
- `NodeDirectoryBinder`, a server-side binder that exposes an application-provided `INodeDirectory`
- `RouteDirectoryClient`, an `IRouteDirectory` client backed by Lakona.Rpc calls
- `RouteDirectoryBinder`, a server-side binder that exposes an application-provided `IRouteDirectory`

It does not provide durable route directory storage, external platform discovery bindings, durable queues, gameplay DTOs, actor migration, or transparent remote actor clients. A route directory service can expose `InMemoryRouteDirectory` for smoke tests, or a project-owned durable implementation for production-specific policy.

## Directory Hosting And Bootstrap

The shared node and route directories are ordinary node-local implementations exposed over the node's advertised cluster endpoint. A data node can own durable directory storage while gateway and battle nodes use `Lakona:Cluster:Seeds` to create remote directory clients.

Example data node that owns the shared directories and business features:

```json
{
  "Lakona": {
    "Node": {
      "Id": "data-1"
    },
    "Feature": [ "state-store", "matchmaking", "leaderboard" ],
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

Example gateway node with only endpoint-local client RPC exposure:

```json
{
  "Lakona": {
    "Node": {
      "Id": "gateway-1"
    },
    "Feature": [],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "json",
        "Host": "0.0.0.0",
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

`Feature` declares cluster-discoverable node capability. `RpcServices` declares services exposed only on that client endpoint. Nodes that do not register a local `INodeDirectory` or `IRouteDirectory` use `Lakona:Cluster:Seeds` as the public bootstrap input and register themselves, client-session routes, and lease refreshes through the remote directory node.

`Lakona:Cluster:Serializer` is required when cluster is configured. Supported
values are `json` and `memorypack`, and all communicating cluster nodes must
use the same value. It selects the node-to-node cluster payload serializer and
does not replace endpoint-local client RPC serializers.

Additional concrete transport factories should be added only with passing cross-process smoke tests. The package exposes `IClusterTransportFactory` so consuming projects can wire custom Lakona.Rpc transport policy while the package keeps the node messaging protocol and status mapping centralized.
