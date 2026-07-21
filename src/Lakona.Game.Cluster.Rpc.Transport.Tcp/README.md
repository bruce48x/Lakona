# Lakona.Game.Cluster.Rpc.Transport.Tcp

TCP transport adapter for the Lakona.Game cluster RPC channel.

```csharp
server.UseClusterRpc(
    TcpClusterRpcTransport.Default,
    clusterSerializer);
```
