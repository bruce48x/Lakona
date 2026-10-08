# Lakona.Game.Clustering.Postgres

PostgreSQL Membership storage for multi-process Lakona clusters. Reference this
Adapter from the stable server application.

## Install and Register

```powershell
dotnet add package Lakona.Game.Clustering.Postgres
```

```csharp
services.AddLakonaPostgresClustering(configuration);
```

See [Membership providers](https://github.com/bruce48x/Lakona/blob/main/docs/cluster.md#providers) for connection
configuration, the packaged `database/postgresql/membership.sql` deployment
step, runtime grants, and upgrade requirements.
