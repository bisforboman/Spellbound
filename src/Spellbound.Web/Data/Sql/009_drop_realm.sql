-- The Realm is scrapped (18 Oct 2026): the game is being designed again from the bottom, and nothing was deployed. Its
-- tables go, and so do its rows in the host's games table. The next game's scripts start at 010. Never edit a released
-- script; add a higher-numbered one.

DROP TABLE IF EXISTS actions;
DROP TABLE IF EXISTS hall_of_fame;
DROP TABLE IF EXISTS news;
DROP TABLE IF EXISTS mages;
DELETE FROM games WHERE mode = 'realm';
