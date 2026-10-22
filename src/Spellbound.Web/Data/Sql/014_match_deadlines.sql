-- Deadlines (docs/discussions/playing-with-friends.md): a step with two or more people in it resolves 24 hours after
-- it began even if someone hasn't committed; they hold (or pass, in the opening). Two misses in a row, or the host
-- handing the seat over, and a bot plays the seat from then on; the person can still watch.
-- Never edit a released script; add a higher-numbered one.

ALTER TABLE match_players
    ADD COLUMN missed int NOT NULL DEFAULT 0,       -- deadlines missed in a row
    ADD COLUMN bot_since timestamptz;               -- set when a bot took over the seat

CREATE TABLE match_deadlines (
    game_id bigint PRIMARY KEY REFERENCES games (id) ON DELETE CASCADE,
    due_at timestamptz NOT NULL                     -- when the current step resolves without whoever hasn't committed
);
CREATE INDEX match_deadlines_due ON match_deadlines (due_at);
