-- Install the current Lakona Membership schema using a deployment account.
-- Stop all nodes first. Existing installations must have the expected marker,
-- columns and live-member index. Runtime accounts need only data access.

BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('lakona-membership-schema', 0));
-- Resolve both Membership tables only in the selected schema.
SELECT set_config('search_path', quote_ident(current_schema()), true);

DO $install$
BEGIN
    IF to_regclass('lakona_membership_cluster') IS NULL
       AND to_regclass('lakona_membership_member') IS NULL THEN
        CREATE TABLE lakona_membership_cluster (
            singleton boolean PRIMARY KEY CHECK (singleton),
            incarnation uuid NOT NULL,
            build_tag text NULL CONSTRAINT ck_lakona_membership_cluster_build_tag
                CHECK (build_tag IS NULL OR build_tag ~ '^[A-Za-z0-9]{1,64}$'),
            version bigint NOT NULL CHECK (version >= 0),
            next_generation bigint NOT NULL CHECK (next_generation > 0)
        );

        CREATE TABLE lakona_membership_member (
            node_id text NOT NULL,
            node_incarnation uuid NOT NULL,
            generation bigint NOT NULL CHECK (generation > 0),
            status smallint NOT NULL,
            entry_version bigint NOT NULL CHECK (entry_version > 0),
            i_am_alive timestamptz NOT NULL,
            payload jsonb NOT NULL,
            PRIMARY KEY (node_id, node_incarnation)
        );

        CREATE UNIQUE INDEX ux_lakona_membership_live_node
            ON lakona_membership_member(node_id) WHERE status <> 3;
        COMMENT ON TABLE lakona_membership_cluster IS 'lakona-membership-schema:1';
    ELSE
        IF obj_description(to_regclass('lakona_membership_cluster'), 'pg_class')
               IS DISTINCT FROM 'lakona-membership-schema:1'
           OR NOT EXISTS (
               SELECT FROM pg_index
               WHERE indrelid = to_regclass('lakona_membership_member')
                 AND indexrelid = to_regclass('ux_lakona_membership_live_node')
                 AND indisunique AND indisvalid AND indnkeyatts = 1
                 AND pg_get_indexdef(indexrelid, 1, false) = 'node_id'
                 AND pg_get_expr(indpred, indrelid) = '(status <> 3)'
           ) THEN
            RAISE EXCEPTION 'Incompatible Lakona Membership schema. Inspect the existing tables before reinstalling.';
        END IF;
    END IF;

    PERFORM singleton, incarnation, build_tag, version, next_generation
    FROM lakona_membership_cluster WHERE FALSE;
    PERFORM node_id, node_incarnation, generation, status, entry_version, i_am_alive, payload
    FROM lakona_membership_member WHERE FALSE;
END
$install$;

COMMIT;
