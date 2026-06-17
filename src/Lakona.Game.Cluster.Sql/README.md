# Lakona.Game.Cluster.Sql

SQL-backed node directory persistence for Lakona.Game cluster membership.

Applications provide their own `DbConnection` factory and SQL provider package.
This package contains provider-neutral node-directory persistence code plus the
initial SQL schema files for the framework-owned node directory table.

## Production Schema Lifecycle

Production applications should not create or alter database tables from ordinary
app startup. Create the node-directory table before app startup through a
deployment migration, DBA-controlled SQL step, or one-time bootstrap job that
uses an admin-capable database account.

The runtime database user used by `SqlNodeDirectory` should not require
`CREATE TABLE` or `ALTER TABLE`. It must have `SELECT`, `INSERT`, and `UPDATE`
on the configured node-directory table. `VerifyReadyAsync` verifies the table
shape and `SELECT` permission; it does not prove write permission.

Initial schema files are packaged under:

```txt
schema/postgres/001-lakona-cluster-nodes.sql
schema/mysql/001-lakona-cluster-nodes.sql
schema/sqlite/001-lakona-cluster-nodes.sql
```

The scripts use the default table name `lakona_cluster_nodes`. If an application
uses a custom table name, call `SqlNodeDirectorySchema.CreateTableSql(dialect,
tableName)` during a controlled migration/bootstrap step and review the emitted
SQL before applying it.

Table names may contain only letters, digits, and underscores. Schema-qualified
table names such as `public.lakona_cluster_nodes` are intentionally rejected to
avoid identifier injection. Use a database user's default schema or connection
search path when the physical database schema must differ.

## Runtime APIs

- `SqlNodeDirectorySchema.CreateTableSql` returns the initial table SQL for
  a dialect and table name. It does not execute SQL.
- `SqlNodeDirectorySchema.EnsureCreatedAsync` executes `CREATE TABLE IF NOT
  EXISTS`. Use it only for tests, local development, or explicit admin/bootstrap
  tools.
- `SqlNodeDirectorySchema.VerifyReadyAsync` performs a zero-row `SELECT`
  against the required columns. It performs no DDL and is appropriate for
  production startup readiness checks.

## Ownership Boundary

This table stores live cluster membership metadata: node id, epoch, state,
endpoints, features, labels, lease expiration, and update time. It is not a
business event log, route directory, gameplay state store, account database, or
leaderboard store.
