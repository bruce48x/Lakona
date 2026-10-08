# Lakona.Game.Clustering.MySql

MySQL Membership storage for multi-process Lakona clusters. Reference this
Adapter from the stable server application.

## Install and Register

```powershell
dotnet add package Lakona.Game.Clustering.MySql
```

```csharp
services.AddLakonaMySqlClustering(configuration);
```

See [Membership providers](https://github.com/bruce48x/Lakona/blob/main/docs/cluster.md#providers) for supported MySQL
storage, connection configuration, the packaged `database/mysql/membership.sql`
deployment step, runtime grants, and upgrade requirements.
