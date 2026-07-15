# Lakona Framework Benchmark Adapter

`frontdoor.echo` maps to Lakona's public generated unary RPC path over
`Lakona.Rpc.Transport.WebSocket`, with MemoryPack serialization. Each load slot
owns one generated `RpcClient` and one persistent WebSocket connection.

The adapter builds against the repository source revision recorded in
`adapter.json`. The terminal node identity is `frontdoor-1`; benchmark responses
return the application request ID and payload unchanged.

`cluster.direct` keeps the same client-facing path, then the front door calls
the configured `worker-1` service through a generated Lakona.Rpc client over
the production TCP transport. The worker returns its own terminal-node
identity through the front door. This uses unary RPC rather than
`IClusterNodeSender`: that API deliberately models one-way delivery and returns
only a `ClusterSendStatus`, while this workload requires the worker's response.

`cluster.routed` registers 256 logical targets in the public
`InMemoryRouteDirectory`. Each request resolves its target, then calls the
resolved owner over the same generated TCP RPC path. FNV-1a ownership is shared
with the Pinus adapter and spans `worker-1` and `worker-2`; the driver validates
the expected owner per request, so a hard-coded destination cannot pass.
