-- Spellbound's tables. Game state is jsonb snapshots of domain objects (DomainJson), keyed by real columns, so adding
-- a field to Mage needs no script. Never edit a released script; add a higher-numbered one.

CREATE TABLE mages (
    round int NOT NULL,
    owner_id text NOT NULL,
    mage_id uuid NOT NULL UNIQUE,         -- the mage's public id, for picking targets
    data jsonb NOT NULL,
    PRIMARY KEY (round, owner_id)         -- one mage per player per round
);
CREATE INDEX mages_owner_id ON mages (owner_id);

CREATE TABLE news (
    round int NOT NULL,
    owner_id text NOT NULL,
    entries jsonb NOT NULL,               -- newest first
    unseen int NOT NULL,                  -- hostile entries the player hasn't seen yet
    PRIMARY KEY (round, owner_id)
);
CREATE INDEX news_owner_id ON news (owner_id);

CREATE TABLE hall_of_fame (
    round int PRIMARY KEY,
    data jsonb NOT NULL
);
