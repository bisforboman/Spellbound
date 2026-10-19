-- Who sits in each match (docs/roadmap.md, M1): a match is a row in the host's games table with its state in jsonb, and
-- this table says which account plays which seat, so a player's matches can be found. A seat with no row is a bot.
-- Never edit a released script; add a higher-numbered one.

CREATE TABLE match_players (
    game_id bigint NOT NULL REFERENCES games (id) ON DELETE CASCADE,
    seat int NOT NULL,                    -- the seat's index in the match (0 is the first seat)
    owner_id text NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    PRIMARY KEY (game_id, seat)
);
CREATE INDEX match_players_owner ON match_players (owner_id);
