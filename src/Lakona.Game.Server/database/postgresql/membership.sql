-- Lakona PostgreSQL Membership schema.
--
-- Stop every Lakona node before applying this file. Run it with a deployment
-- account which owns the target schema. Game-server runtime accounts need only
-- data access and must not receive CREATE, ALTER, or DROP privileges.
--
-- This is the single schema and upgrade entry point. It is safe to execute
-- repeatedly. Future schema changes belong in this transaction as conditional,
-- convergent SQL.

BEGIN;

-- Serialize two deployment jobs which target the same PostgreSQL database.
SELECT pg_advisory_xact_lock(hashtextextended('lakona-membership-schema', 0));

-- Lakona versions before logical cluster namespaces were removed used
-- cluster_id columns. Membership metadata is process-lifetime coordination
-- data, so a stopped cluster can safely replace that incompatible layout.
DO $migration$
BEGIN
    IF (
        to_regclass('lakona_membership_cluster') IS NOT NULL
        AND NOT EXISTS (
            SELECT 1
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'lakona_membership_cluster'
              AND column_name = 'singleton'
        )
    ) OR EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND table_name IN ('lakona_membership_cluster', 'lakona_membership_member')
          AND column_name = 'cluster_id'
    ) THEN
        DROP TABLE IF EXISTS lakona_membership_member;
        DROP TABLE IF EXISTS lakona_membership_cluster;
    END IF;
END
$migration$;

CREATE TABLE IF NOT EXISTS lakona_membership_cluster (
    singleton boolean PRIMARY KEY CHECK (singleton),
    incarnation uuid NOT NULL,
    build_tag text NULL,
    version bigint NOT NULL CHECK (version >= 0),
    next_generation bigint NOT NULL CHECK (next_generation > 0)
);

ALTER TABLE lakona_membership_cluster
    ADD COLUMN IF NOT EXISTS build_tag text NULL;

DO $constraint$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'lakona_membership_cluster'::regclass
          AND conname = 'ck_lakona_membership_cluster_build_tag'
    ) THEN
        ALTER TABLE lakona_membership_cluster
            ADD CONSTRAINT ck_lakona_membership_cluster_build_tag
            CHECK (build_tag IS NULL OR build_tag ~ '^[A-Za-z0-9]{1,64}$');
    END IF;
END
$constraint$;

CREATE TABLE IF NOT EXISTS lakona_membership_member (
    node_id text NOT NULL,
    node_incarnation uuid NOT NULL,
    generation bigint NOT NULL CHECK (generation > 0),
    status smallint NOT NULL,
    entry_version bigint NOT NULL CHECK (entry_version > 0),
    i_am_alive timestamptz NOT NULL,
    payload jsonb NOT NULL,
    PRIMARY KEY (node_id, node_incarnation)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_lakona_membership_live_node
    ON lakona_membership_member(node_id) WHERE status <> 3;

-- Fail this deployment transaction if the resulting schema cannot satisfy the
-- runtime's read/write contract. CREATE TABLE IF NOT EXISTS alone cannot detect
-- an existing table with an incompatible shape.
DO $validation$
BEGIN
    PERFORM singleton, incarnation, build_tag, version, next_generation
    FROM lakona_membership_cluster
    WHERE FALSE;

    PERFORM node_id, node_incarnation, generation, status, entry_version,
            i_am_alive, payload
    FROM lakona_membership_member
    WHERE FALSE;
END
$validation$;

-- The runtime reads this marker to distinguish a schema installed by this
-- complete transaction from tables which merely happen to use the same names.
COMMENT ON TABLE lakona_membership_cluster IS 'lakona-membership-schema:1';

COMMIT;
