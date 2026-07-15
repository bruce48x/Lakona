# Lakona Framework Benchmark Adapter

Slice 2 maps `frontdoor.echo` to Lakona's public generated unary RPC path over
`Lakona.Rpc.Transport.WebSocket`, with MemoryPack serialization. Each load slot
owns one generated `RpcClient` and one persistent WebSocket connection.

The adapter builds against the repository source revision recorded in
`adapter.json`. The terminal node identity is `frontdoor-1`; benchmark responses
return the application request ID and payload unchanged.
