-- Web Push: the browsers (phones) where a player turned notifications on. The endpoint is an address the browser's
-- push service gave; p256dh and auth are the browser's keys, so only that browser can read what's sent.
-- Never edit a released script; add a higher-numbered one.

CREATE TABLE push_subscriptions (
    endpoint text PRIMARY KEY,
    owner_id text NOT NULL,
    p256dh text NOT NULL,
    auth text NOT NULL,
    created_at timestamptz NOT NULL
);
CREATE INDEX push_subscriptions_owner ON push_subscriptions (owner_id);
