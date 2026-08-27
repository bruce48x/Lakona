# Lakona.Game.Clustering.Postgres

`Lakona.Game.Clustering.Postgres` stores Lakona cluster membership in PostgreSQL.
Reference it from the stable server application and register it after the core
game-server services:

```csharp
services.AddLakonaPostgresClustering(configuration);
```

Set `Lakona:Cluster:Membership:Provider` to `Postgres` and provide the runtime
connection under the configured `ConnectionStringName`. Before starting or
upgrading a cluster, apply the packaged
`database/postgresql/membership.sql` with a separate deployment account. The
runtime account needs data access only and must not receive DDL privileges.
