-- Lakona.Game.Cluster.Sql SQLite initial schema for SQL-backed cluster node directory.
-- This script is intended for local validation and tests. Production SQLite deployments should still run schema setup outside ordinary app startup.

CREATE TABLE IF NOT EXISTS lakona_cluster_nodes (
    cluster_name TEXT NOT NULL,
    node_id TEXT NOT NULL,
    node_epoch INTEGER NOT NULL,
    state INTEGER NOT NULL,
    endpoints_json TEXT NOT NULL,
    features_json TEXT NOT NULL,
    labels_json TEXT NOT NULL,
    lease_expires_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (cluster_name, node_id)
);
