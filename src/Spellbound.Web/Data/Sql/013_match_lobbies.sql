-- Invites (docs/discussions/playing-with-friends.md): a host opens a lobby with a number of seats and shares its link;
-- whoever opens the link takes a free seat. When the host starts, the match is made (a games row and its
-- match_players) and the lobby keeps its id; bots take the seats nobody took. A host deleting their account takes their
-- lobbies with it, on purpose: nobody else can start them. Never edit a released script; add a higher-numbered one.

CREATE TABLE match_lobbies (
    code text PRIMARY KEY,                -- the invite link's secret part
    host_id text NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    seats int NOT NULL,                   -- the match's size, host and bots included
    created_at timestamptz NOT NULL,
    game_id bigint REFERENCES games (id) ON DELETE CASCADE   -- the match, once the host has started it
);
CREATE INDEX match_lobbies_host ON match_lobbies (host_id) WHERE game_id IS NULL;

CREATE TABLE match_lobby_members (
    code text NOT NULL REFERENCES match_lobbies (code) ON DELETE CASCADE,
    owner_id text NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    joined_at timestamptz NOT NULL,       -- the seat order: the host first, then whoever joined first
    PRIMARY KEY (code, owner_id)          -- one seat per account
);
CREATE INDEX match_lobby_members_owner ON match_lobby_members (owner_id);
