-- How each player finished each Skirmish: the win record and the leaderboard. Written once, when the game ends, for
-- every seat that still has an account. Never edit a released script; add a higher-numbered one.

CREATE TABLE skirmish_results (
    game_id bigint NOT NULL REFERENCES skirmish_games (game_id),
    owner_id text NOT NULL,
    seat int NOT NULL,
    name text NOT NULL,                   -- the mage's name in that game: what the leaderboard shows
    place int NOT NULL,                   -- 1 for the winner
    points int NOT NULL,
    won boolean NOT NULL,
    ended_at timestamptz NOT NULL,
    PRIMARY KEY (game_id, owner_id)
);
CREATE INDEX skirmish_results_owner ON skirmish_results (owner_id);
