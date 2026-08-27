# Lakona.Game.Clustering.MySql

`Lakona.Game.Clustering.MySql` stores Lakona cluster membership in MySQL 8 or
a compatible managed MySQL service. InnoDB transactions preserve the same
Membership CAS and fencing contract as the other production Adapters.

```csharp
services.AddLakonaMySqlClustering(configuration);
```

Set `Lakona:Cluster:Membership:Provider` to `MySql` and point
`ConnectionStringName` at the runtime connection string. Before starting game
servers, apply the package's single `database/mysql/membership.sql` file with a
deployment account. Runtime credentials need data access only and must not own
or alter the schema.
