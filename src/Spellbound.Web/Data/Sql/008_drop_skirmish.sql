-- Skirmish is removed (18 Oct 2026: the Realm is being reworked as Mageborn, docs/realm-rework.md). Its tables go,
-- children first, and so do its rows in the host's games table. Never edit a released script; add a higher-numbered one.

DROP TABLE IF EXISTS skirmish_lobby_players;
DROP TABLE IF EXISTS skirmish_lobbies;
DROP TABLE IF EXISTS skirmish_results;
DROP TABLE IF EXISTS skirmish_rounds;
DROP TABLE IF EXISTS skirmish_seats;
DROP TABLE IF EXISTS skirmish_games;
DELETE FROM games WHERE mode = 'skirmish';
