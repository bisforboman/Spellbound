-- The game host's tables: accounts, the round being played, bans and multi-account signals.
-- Never edit a released script; add a higher-numbered one.

CREATE TABLE accounts (
    id text PRIMARY KEY,                  -- our own id: a hash of provider and subject (AccountService.IdFor)
    provider text NOT NULL,               -- "google", or "dev" in Development
    subject text NOT NULL,                -- the provider's id for the person
    email text NOT NULL,
    created_at timestamptz NOT NULL,
    last_sign_in_at timestamptz NOT NULL,
    UNIQUE (provider, subject)
);
CREATE INDEX accounts_email ON accounts (upper(email));

CREATE TABLE current_round (
    id int PRIMARY KEY CHECK (id = 1),    -- one row
    number int NOT NULL,
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL
);

CREATE TABLE bans (
    owner_id text PRIMARY KEY,
    at timestamptz NOT NULL,
    reason text NOT NULL,
    banned_by text NOT NULL               -- the admin's email
);

CREATE TABLE account_networks (
    owner_id text NOT NULL,
    network text NOT NULL,                -- a keyed hash of the address, never the address
    last_seen timestamptz NOT NULL,
    PRIMARY KEY (owner_id, network)
);
CREATE INDEX account_networks_last_seen ON account_networks (last_seen);

CREATE TABLE server_secrets (
    name text PRIMARY KEY,
    value bytea NOT NULL
);
