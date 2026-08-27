# Lakona.Game.Clustering.Redis

`Lakona.Game.Clustering.Redis` stores Lakona cluster membership in one Redis
Hash. Atomic Lua operations preserve the same Membership CAS and fencing
contract as the PostgreSQL Adapter.

```csharp
services.AddLakonaRedisClustering(configuration);
```

Set `Lakona:Cluster:Membership:Provider` to `Redis` and point
`ConnectionStringName` at the Redis connection string. Membership keys never
expire. Production Redis must use `noeviction`, persistence, authentication,
TLS where required, and a monitored high-availability topology. The default
key is `lakona:{membership}:table`; the hash tag keeps every atomic operation
in one Redis Cluster slot.
