CREATE TABLE IF NOT EXISTS agar_users (
    user_id varchar(128) PRIMARY KEY,
    password_hash varchar(128) NOT NULL,
    login_count integer NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    last_login_at_utc timestamp with time zone NOT NULL,
    win_count integer NOT NULL,
    victory_points integer NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);
