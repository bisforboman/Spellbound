-- Commit-and-wait rounds (docs/roadmap.md, M2): a player's committed bid or orders wait on their seat's row until every
-- person in the match has committed, then the step resolves. Kept per seat so nobody else's choice is ever loaded
-- with the match. Never edit a released script; add a higher-numbered one.

ALTER TABLE match_players
    ADD COLUMN pending jsonb,             -- the committed bid or orders, as the domain's JSON
    ADD COLUMN pending_for text;          -- the step it was committed for ("opening-2", "round-3"); stale otherwise
