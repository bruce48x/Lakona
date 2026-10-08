# Lakona.Game.Testing

Run multiple real Lakona server nodes in one test process, using shared
in-memory Membership and programmable cluster transport. Use this package for
node lifecycle, routing, Actor Directory, and recovery integration tests.

## Install

```powershell
dotnet add package Lakona.Game.Testing
```

## Minimal Cluster

```csharp
await using var cluster = new LakonaTestClusterBuilder()
    .AddNode("data-1", "data")
    .AddNode("battle-1", "battle")
    .ConfigureNodes(node =>
        node.UseHotfixAssembly(typeof(GameHotfixStartup).Assembly))
    .Build();
await cluster.StartAsync();
await cluster.WaitForMembershipAsync();
```

`GameHotfixStartup` is the application's Hotfix entry type.

## Guides

- [In-process TestCluster](https://github.com/bruce48x/Lakona/blob/main/docs/cluster.md#in-process-testcluster): role-scoped dependencies, network faults, Membership-view controls, Hotfix service access, and cleanup.
- [Testing coverage](https://github.com/bruce48x/Lakona/blob/main/docs/contributing/testing.md#multi-node-integration-tests): choosing in-process, provider, and process/container tests.

Use provider and process/container tests for real storage, sockets, TLS, and
separate-process failures; TestCluster covers the in-process coordination layer.
