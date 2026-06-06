# Cluster.TwoNode

`Cluster.TwoNode` is a minimal cross-process Lakona.Game cluster smoke sample.

It starts:

- a directory process exposing `INodeDirectory` through `ULinkRpcNodeDirectoryBinder` and `IRouteDirectory` through `ULinkRpcRouteDirectoryBinder`
- a worker process exposing `IClusterMessageHandler` through `ULinkRpcClusterMessageBinder`
- a driver process that registers a local route, sends local and remote cluster messages, clears a stale worker epoch, restarts the worker so the node directory assigns a new epoch, and sends again

The worker registers with the node directory before publishing routes. The assigned `NodeEpoch` is then used for route ownership so a restarted worker cannot inherit stale routes from its previous process.

Run:

```powershell
dotnet run --project samples/Cluster.TwoNode/Cluster.TwoNode.csproj -- --mode driver
```

The sample intentionally contains no matchmaking, room rules, account system, persistence schema, or gameplay DTOs. It only verifies cluster infrastructure boundaries and failure statuses.
