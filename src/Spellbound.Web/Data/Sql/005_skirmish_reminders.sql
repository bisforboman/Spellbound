-- Skirmish reminders: a seat is nudged once a round when it's the last one not ready, and a game is reminded once a
-- round when its time is running out. Never edit a released script; add a higher-numbered one.

ALTER TABLE skirmish_seats ADD COLUMN nudged boolean NOT NULL DEFAULT false;
ALTER TABLE skirmish_games ADD COLUMN reminded_round int NOT NULL DEFAULT 0; -- the round last reminded about
