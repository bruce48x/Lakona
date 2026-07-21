# Lakona.Game.Cluster.Rpc

`Lakona.Game.Cluster.Rpc` is the transport- and serializer-neutral RPC layer
for Lakona.Game node messaging, replicated membership, route lookup,
notification relay, and remote Actor dispatch.

The package owns the cluster protocol, pooled clients, server binders, and the
`ClusterRpcChannel` that keeps inbound and outbound behavior consistent. A
server selects one transport adapter and one serializer adapter at its
composition root:

```csharp
server.UseClusterRpc(clusterTransport, clusterSerializer);
```

Install concrete adapters separately. Official packages are:

- `Lakona.Game.Cluster.Rpc.Transport.Tcp`
- `Lakona.Game.Cluster.Rpc.Serializer.Json`
- `Lakona.Game.Cluster.Rpc.Serializer.MemoryPack`

For example:

```csharp
using Lakona.Game.Cluster.Rpc.Serializer.MemoryPack;
using Lakona.Game.Cluster.Rpc.Transport.Tcp;

server.UseClusterRpc(
    TcpClusterRpcTransport.Default,
    MemoryPackClusterRpcSerializer.Default);
```

`Lakona:Cluster` configures the local endpoint, `Lakona:Cluster:Seeds`
discovery contacts, and bootstrap policy. It does not contain a serializer or
transport selector. The selected transport must handle the endpoint URI
scheme.

Before RPC starts, peers exchange a small fixed-format negotiation frame. A
serializer protocol mismatch is rejected before either RPC serializer decodes
a payload. Negotiation costs one round trip when a connection is established;
steady calls reuse the pooled connection.

Custom transports implement `IClusterRpcTransport` and own both outbound
connections and the inbound listener. Custom serializers implement
`IClusterRpcSerializer` and expose a stable protocol ID plus an
`IRpcSerializer` factory.

This package does not own gameplay DTOs, client-facing endpoints, durable game
state, Actor migration, or transport-specific security policy.
