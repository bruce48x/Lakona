-- Lakona.Game.Cluster.Sql MySQL initial schema for SQL-backed cluster node directory.
-- Execute through a deployment migration, DBA process, or explicit admin bootstrap step before production app startup.
-- Ordinary production app users should not require CREATE TABLE permission.

CREATE TABLE IF NOT EXISTS lakona_cluster_nodes (
    cluster_name VARCHAR(256) NOT NULL,
    node_id VARCHAR(256) NOT NULL,
    node_epoch BIGINT NOT NULL,
    state INT NOT NULL,
    endpoints_json TEXT NOT NULL,
    actor_hosts_json TEXT NOT NULL,
    labels_json TEXT NOT NULL,
    lease_expires_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (cluster_name, node_id)
);
