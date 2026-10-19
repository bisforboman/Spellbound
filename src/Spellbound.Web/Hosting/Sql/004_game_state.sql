-- A game's state beside its rules: games played on demand keep their whole state in their own row, as the game wrote
-- it; the host never reads it. Null for a game that keeps its state elsewhere.
-- Never edit a released script; add a higher-numbered one.

ALTER TABLE games ADD COLUMN state jsonb;
