# Lakona.Game.Cluster.Rpc

`Lakona.Game.Cluster.Rpc` contains the Lakona.Rpc adapter layer for explicit Lakona.Game cluster node-to-node messaging, remote node-directory calls, and remote route-directory calls.

The package stays outside `Lakona.Game.Cluster` so core route contracts remain transport-neutral. It provides:

- a Lakona.Rpc method contract for sending `ClusterMessage` envelopes between nodes
- `ULinkRpcClusterNodeMessenger`, an `INodeMessenger` implementation backed by a Lakona.Rpc client factory
- `ULinkRpcClusterClientFactory`, a reusable client cache over application-provided Lakona.Rpc transports
- `IULinkRpcClusterTransportFactory`, the boundary where projects choose TCP, WebSocket, KCP, security, and endpoint policy
- `TcpULinkRpcClusterTransportFactory`, a TCP transport factory for endpoint addresses such as `tcp://127.0.0.1:20010`
- `ULinkRpcClusterMessageBinder`, a server-side binder that dispatches inbound cluster messages into an `IClusterMessageHandler`
- `ULinkRpcNodeDirectory`, an `INodeDirectory` client backed by Lakona.Rpc calls
- `ULinkRpcNodeDirectoryBinder`, a server-side binder that exposes an application-provided `INodeDirectory`
- `ULinkRpcRouteDirectory`, an `IRouteDirectory` client backed by Lakona.Rpc calls
- `ULinkRpcRouteDirectoryBinder`, a server-side binder that exposes an application-provided `IRouteDirectory`

It does not provide durable route directory storage, external platform discovery bindings, durable queues, gameplay DTOs, actor migration, or transparent remote actor clients. A route directory service can expose `InMemoryRouteDirectory` for smoke tests, or a project-owned durable implementation for production-specific policy.

## Node-Directory Hosting

The node-directory service is configured like any other node-local service. The Lakona.Rpc adapter makes that service reachable over the node's advertised cluster endpoint; it does not decide whether the service runs in an all-in-one development node, a dedicated control node, or a co-located production node.

Example all-in-one development node:

```json
{
  "Cluster": {
    "NodeId": "dev-1",
    "AdvertisedEndpoints": {
      "cluster": "tcp://127.0.0.1:21000"
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
      { "Kind": "room", "Name": "room" }
    ]
  }
}
```

Example production control node:

```json
{
  "Cluster": {
    "NodeId": "control-1",
    "AdvertisedEndpoints": {
      "cluster": "tcp://10.0.0.10:21000"
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
    ]
  }
}
```

Other nodes should use `Cluster:Bootstrap:NodeDirectoryEndpoints` to find one or more configured directory nodes, then register their own `NodeId`, advertised endpoints, service descriptors, and lease.

Additional concrete transport factories should be added only with passing cross-process smoke tests. The package exposes `IULinkRpcClusterTransportFactory` so consuming projects can wire custom Lakona.Rpc transport policy while the package keeps the node messaging protocol and status mapping centralized.
