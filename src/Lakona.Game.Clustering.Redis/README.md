# Lakona.Game.Clustering.Redis

Redis Membership storage for multi-process Lakona clusters. Reference this
Adapter from the stable server application.

## Install and Register

```powershell
dotnet add package Lakona.Game.Clustering.Redis
```

```csharp
services.AddLakonaRedisClustering(configuration);
```

See [Membership providers](https://github.com/bruce48x/Lakona/blob/main/docs/cluster.md#providers) for connection and key
configuration, atomic updates, persistence, eviction policy, and production
deployment requirements.
