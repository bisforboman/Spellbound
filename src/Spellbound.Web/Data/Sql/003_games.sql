-- Spellbound's rows belong to a game (the host's games table) instead of a round number. Realm rounds that were played
-- before games existed become ended Realm games with their round number as id, so no row has to change its key value.
-- Never edit a released script; add a higher-numbered one.

UPDATE games SET mode = 'realm' WHERE mode = '';

-- Earlier rounds only exist in Spellbound's own tables; their times are kept in their hall of fame, if any.
INSERT INTO games (id, mode, number, starts_at, ends_at, ended_at, rules, created_at)
SELECT r.round, 'realm', r.round,
       coalesce((f.data ->> 'startedAt')::timestamptz, 'epoch'),
       coalesce((f.data ->> 'endedAt')::timestamptz, 'epoch'),
       coalesce((f.data ->> 'endedAt')::timestamptz, 'epoch'),
       '{}',
       coalesce((f.data ->> 'startedAt')::timestamptz, 'epoch')
FROM (SELECT round FROM mages UNION SELECT round FROM news UNION SELECT round FROM hall_of_fame
      UNION SELECT round FROM actions) r
LEFT JOIN hall_of_fame f ON f.round = r.round
WHERE NOT EXISTS (SELECT 1 FROM games g WHERE g.id = r.round);

-- New games get ids after the migrated ones.
SELECT setval(pg_get_serial_sequence('games', 'id'), coalesce((SELECT max(id) FROM games), 0) + 1, false);

ALTER TABLE mages RENAME COLUMN round TO game_id;
ALTER TABLE mages ALTER COLUMN game_id TYPE bigint;
ALTER TABLE news RENAME COLUMN round TO game_id;
ALTER TABLE news ALTER COLUMN game_id TYPE bigint;
ALTER TABLE hall_of_fame RENAME COLUMN round TO game_id;
ALTER TABLE hall_of_fame ALTER COLUMN game_id TYPE bigint;
ALTER TABLE actions RENAME COLUMN round TO game_id;
ALTER TABLE actions ALTER COLUMN game_id TYPE bigint;
