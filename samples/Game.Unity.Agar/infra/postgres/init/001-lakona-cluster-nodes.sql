-- Local Docker init SQL for the Agar sample data node.
-- Production deployments should run equivalent schema through a versioned migration or DBA-controlled deployment step.

CREATE TABLE IF NOT EXISTS lakona_cluster_nodes (
    cluster_name TEXT NOT NULL,
    node_id TEXT NOT NULL,
    node_epoch BIGINT NOT NULL,
    state INTEGER NOT NULL,
    endpoints_json TEXT NOT NULL,
    actor_hosts_json TEXT NOT NULL,
    labels_json TEXT NOT NULL,
    lease_expires_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (cluster_name, node_id)
);
