-- Install the current Lakona Membership schema using a deployment account.
-- Stop all nodes first. Run in a dedicated connection and close it on error
-- to release the deployment lock and temporary validation table.
-- Runtime accounts need only data access.

CREATE TEMPORARY TABLE lakona_membership_install_check (
    lock_acquired INT NOT NULL,
    schema_compatible INT NOT NULL,
    CONSTRAINT ck_membership_deployment_lock CHECK (lock_acquired = 1),
    CONSTRAINT ck_membership_compatible_schema CHECK (schema_compatible = 1)
);

INSERT INTO lakona_membership_install_check
VALUES (GET_LOCK(CONCAT('lakona-membership:', MD5(DATABASE())), 60), 1);

-- Check existing objects before issuing any persistent DDL. MySQL commits DDL
-- statements individually, so a partial installation also requires inspection.
INSERT INTO lakona_membership_install_check
SELECT 1, COUNT(*) = 0 OR (
    COUNT(*) = 2
    AND COUNT(CASE WHEN table_name = 'lakona_membership_cluster'
                    AND BINARY table_comment = 'lakona-membership-schema:1' THEN 1 END) = 1
    AND EXISTS (
        SELECT 1 FROM information_schema.statistics
        WHERE table_schema = DATABASE() AND table_name = 'lakona_membership_member'
          AND index_name = 'ux_lakona_membership_live_node'
        GROUP BY index_name
        HAVING COUNT(*) = 1 AND MAX(non_unique) = 0
           AND MAX(column_name) = 'live_node_id' AND MAX(sub_part) IS NULL
    )
)
FROM information_schema.tables
WHERE table_schema = DATABASE()
  AND table_name IN ('lakona_membership_cluster', 'lakona_membership_member');

CREATE TABLE IF NOT EXISTS lakona_membership_cluster (
    singleton TINYINT UNSIGNED NOT NULL,
    incarnation CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    build_tag VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    version BIGINT NOT NULL,
    next_generation BIGINT NOT NULL,
    PRIMARY KEY (singleton),
    CONSTRAINT ck_lakona_membership_cluster_singleton CHECK (singleton = 1),
    CONSTRAINT ck_lakona_membership_cluster_version CHECK (version >= 0),
    CONSTRAINT ck_lakona_membership_cluster_generation CHECK (next_generation > 0),
    CONSTRAINT ck_lakona_membership_cluster_build_tag CHECK (
        build_tag IS NULL OR build_tag REGEXP '^[A-Za-z0-9]{1,64}$')
) ENGINE=InnoDB COMMENT='lakona-membership-schema:1';

CREATE TABLE IF NOT EXISTS lakona_membership_member (
    node_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    node_incarnation CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    generation BIGINT NOT NULL,
    status SMALLINT NOT NULL,
    entry_version BIGINT NOT NULL,
    i_am_alive BIGINT NOT NULL,
    payload JSON NOT NULL,
    live_node_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
        GENERATED ALWAYS AS (CASE WHEN status <> 3 THEN node_id ELSE NULL END) STORED,
    PRIMARY KEY (node_id, node_incarnation),
    UNIQUE KEY ux_lakona_membership_live_node (live_node_id),
    KEY ix_lakona_membership_cleanup (status, i_am_alive),
    CONSTRAINT ck_lakona_membership_member_generation CHECK (generation > 0),
    CONSTRAINT ck_lakona_membership_member_version CHECK (entry_version > 0)
) ENGINE=InnoDB;

SELECT singleton, incarnation, build_tag, version, next_generation
FROM lakona_membership_cluster
WHERE 1 = 0;

SELECT node_id, node_incarnation, generation, status, entry_version,
       i_am_alive, payload, live_node_id
FROM lakona_membership_member
WHERE 1 = 0;

DROP TEMPORARY TABLE lakona_membership_install_check;
SELECT RELEASE_LOCK(CONCAT('lakona-membership:', MD5(DATABASE())));
