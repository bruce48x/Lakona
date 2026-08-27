-- Lakona MySQL Membership schema.
--
-- Stop every Lakona node before applying this file. Run it with a deployment
-- account which owns the target database. Game-server runtime accounts need
-- only data access and must not receive CREATE, ALTER, or DROP privileges.
--
-- This is the single schema and upgrade entry point. It is safe to execute
-- repeatedly. Future schema changes belong in this file as conditional,
-- convergent SQL.

SELECT GET_LOCK(CONCAT('lakona-membership:', MD5(DATABASE())), 60);

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

ALTER TABLE lakona_membership_cluster
    COMMENT = 'lakona-membership-schema:1';

-- Fail when existing tables merely share these names but cannot satisfy the
-- runtime contract. CREATE TABLE IF NOT EXISTS does not validate their shape.
SELECT singleton, incarnation, build_tag, version, next_generation
FROM lakona_membership_cluster
WHERE 1 = 0;

SELECT node_id, node_incarnation, generation, status, entry_version,
       i_am_alive, payload, live_node_id
FROM lakona_membership_member
WHERE 1 = 0;

SELECT RELEASE_LOCK(CONCAT('lakona-membership:', MD5(DATABASE())));
