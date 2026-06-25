# Lakona.Game.Cluster.Rpc.MemoryPack

MemoryPack formatter registration package for the framework DTOs used by
`Lakona.Game.Cluster.Rpc`.

## Install

```bash
dotnet add package Lakona.Game.Cluster.Rpc.MemoryPack
```

Use this package when a cluster endpoint is configured to use MemoryPack for
node-to-node RPC. It keeps the cluster RPC DTO formatter registration separate
from the transport adapter and from application gameplay contracts.

## Usage

Register the framework formatters before cluster RPC traffic is serialized:

```csharp
using Lakona.Game.Cluster.Rpc.MemoryPack;

ClusterRpcMemoryPack.RegisterFormatters();
```

## Advanced Usage

Create a ready-to-use MemoryPack RPC serializer after registering the cluster
formatters:

```csharp
using Lakona.Game.Cluster.Rpc.MemoryPack;

var serializer = ClusterRpcMemoryPack.CreateSerializer();
```

Application DTOs still own their own MemoryPack attributes or formatter
registration. This package only covers Lakona cluster RPC framework DTOs.
