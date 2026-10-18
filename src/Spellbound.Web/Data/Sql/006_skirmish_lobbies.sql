-- Skirmish lobbies: a game a player has set up, waiting for players. It starts (a skirmish_games row) when it's full,
-- or when its creator starts it with whoever has joined. Never edit a released script; add a higher-numbered one.

CREATE TABLE skirmish_lobbies (
    id bigserial PRIMARY KEY,
    code text NOT NULL UNIQUE,            -- the invite code, in capitals; the link is /join/{code}
    creator_id text,                      -- null once the creator's account is deleted (only kept for started games)
    rules jsonb NOT NULL,                 -- the SkirmishRules chosen; Players is how many seats it waits for
    round_length interval NOT NULL,
    public boolean NOT NULL,              -- listed on the Games page for anyone to join
    created_at timestamptz NOT NULL,
    game_id bigint REFERENCES skirmish_games (game_id) -- the game, once started
);
CREATE INDEX skirmish_lobbies_open ON skirmish_lobbies (created_at) WHERE game_id IS NULL;

-- Who has joined, under which name and school. Emptied when the game starts: the seats move to skirmish_seats.
CREATE TABLE skirmish_lobby_players (
    lobby_id bigint NOT NULL REFERENCES skirmish_lobbies (id) ON DELETE CASCADE,
    owner_id text NOT NULL,
    name text NOT NULL,
    school text NOT NULL,
    joined_at timestamptz NOT NULL,
    PRIMARY KEY (lobby_id, owner_id)
);
CREATE INDEX skirmish_lobby_players_owner ON skirmish_lobby_players (owner_id);
