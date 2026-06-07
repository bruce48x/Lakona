CREATE TABLE IF NOT EXISTS agar_grain_state (
    service_id varchar(150) NOT NULL,
    provider_name varchar(150) NOT NULL,
    state_name varchar(150) NOT NULL,
    grain_id varchar(512) NOT NULL,
    payload bytea NOT NULL,
    version bigint NOT NULL,
    modified_on_utc timestamp without time zone NOT NULL,
    CONSTRAINT pk_agar_grain_state PRIMARY KEY (service_id, provider_name, state_name, grain_id)
);
