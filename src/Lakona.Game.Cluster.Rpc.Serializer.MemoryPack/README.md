# Lakona.Game.Cluster.Rpc.Serializer.MemoryPack

MemoryPack serializer adapter and generated framework formatter catalog for the
Lakona.Game cluster RPC channel.

## Install

```bash
dotnet add package Lakona.Game.Cluster.Rpc.Serializer.MemoryPack
```

## Usage

```csharp
using Lakona.Game.Cluster.Rpc.Serializer.MemoryPack;
using Lakona.Game.Cluster.Rpc.Transport.Tcp;

server.UseClusterRpc(
    TcpClusterRpcTransport.Default,
    MemoryPackClusterRpcSerializer.Default);
```

For custom MemoryPack options, create an adapter instance:

```csharp
var clusterSerializer = new MemoryPackClusterRpcSerializer(options);
```

The adapter registers the generated cluster protocol formatters when it creates
the serializer. Application DTOs still own their MemoryPack attributes or
custom formatter registration.
