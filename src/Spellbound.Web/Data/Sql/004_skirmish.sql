-- Skirmish games: a games row (mode 'skirmish', rules = SkirmishRules) plus the game's state, its seats and a record of
-- every round played. Never edit a released script; add a higher-numbered one.

CREATE TABLE skirmish_games (
    game_id bigint PRIMARY KEY REFERENCES games (id),
    seed int NOT NULL,                    -- the board, deck and objectives come from it
    state jsonb NOT NULL,                 -- the SkirmishGame as it is now
    round_length interval NOT NULL,       -- the time limit for each round
    round_ends_at timestamptz NOT NULL,   -- when the current round plays if not everyone is ready by then
    ended boolean NOT NULL DEFAULT false
);
CREATE INDEX skirmish_games_due ON skirmish_games (round_ends_at) WHERE NOT ended;

-- The players: who sits in which seat, the orders they've saved for the current round, and whether they're ready.
CREATE TABLE skirmish_seats (
    game_id bigint NOT NULL REFERENCES skirmish_games (game_id),
    seat int NOT NULL,
    owner_id text,                        -- null once the account is deleted; the seat plays on and goes away
    orders jsonb NOT NULL DEFAULT '[]',
    ready boolean NOT NULL DEFAULT false,
    PRIMARY KEY (game_id, seat),
    UNIQUE (game_id, owner_id)
);
CREATE INDEX skirmish_seats_owner ON skirmish_seats (owner_id);

-- Every round played: the orders, who was ready, the random draws and the result, so a round can be shown afterwards
-- and replayed exactly from the state before it.
CREATE TABLE skirmish_rounds (
    game_id bigint NOT NULL REFERENCES skirmish_games (game_id),
    round int NOT NULL,
    played_at timestamptz NOT NULL,
    orders jsonb NOT NULL,                -- by seat
    ready jsonb NOT NULL,                 -- the seats that were ready
    draws jsonb NOT NULL,
    report jsonb NOT NULL,                -- the SkirmishRoundReport
    state_after jsonb NOT NULL,
    rules_hash text NOT NULL,
    PRIMARY KEY (game_id, round)
);
