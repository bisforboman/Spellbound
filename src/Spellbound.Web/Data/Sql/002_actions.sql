-- The action log: one row per successful player action, written in the same transaction as the action itself.
-- With the mage before an action (an earlier row's replay, or the "create" row), the inputs, the random draws and the
-- same rules, the action can be replayed exactly. For disputes, audits and balance data from real play.

CREATE TABLE actions (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    round int NOT NULL,
    owner_id text NOT NULL,
    at timestamptz NOT NULL,              -- the time the domain saw (the action's "now")
    action text NOT NULL,                 -- create, explore, build, meditate, cast, recruit, attack, hex
    target_owner_id text,                 -- the other mage's owner, for attacks and hexes
    inputs jsonb NOT NULL,                -- what the player chose, e.g. {"turns": 5}
    draws jsonb NOT NULL,                 -- the random numbers the action used, in order
    result jsonb NOT NULL,                -- the report lines, and the target's for attacks and hexes
    state jsonb NOT NULL,                 -- the mage's main numbers afterwards, for balance queries
    target_state jsonb,                   -- the target's, for attacks and hexes
    rules_hash text NOT NULL              -- which GameRules were in force (they can be tuned mid-round)
);
-- Owner first, so deleting an account (which spans rounds) uses them too.
CREATE INDEX actions_owner ON actions (owner_id, round, at);
CREATE INDEX actions_target ON actions (target_owner_id, round, at) WHERE target_owner_id IS NOT NULL;
CREATE INDEX actions_round ON actions (round, at);
