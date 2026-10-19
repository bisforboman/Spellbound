-- Games are matches started on demand now (docs/roadmap.md, M1), so the placeholder's always-on round, if one is still
-- running, is ended. Never edit a released script; add a higher-numbered one.

UPDATE games SET ended_at = now() WHERE mode = 'placeholder' AND ended_at IS NULL;
