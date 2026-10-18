# Spellbound architecture

## Stack

- **.NET 10**, with **Blazor Server** (interactive server rendering) for the UI, all in C#
- **Sign-in with Google** (`Microsoft.AspNetCore.Authentication.Google`) and a cookie; no passwords are stored
- **PostgreSQL 16** through plain **Npgsql** (SQL, no ORM) for accounts and, later, game data. It runs
  in its own container next to the app
- **xUnit** for domain tests, plus `WebApplicationFactory` integration tests for the web app

**The game was scrapped on 18 Oct 2026** to be designed again from the bottom (see the roadmap). What remains is the
platform it ran on. The old game (the Realm and the Mageborn rework) is in the git history at `b171e46`.

Blazor Server keeps state on the server and sends small UI diffs over SignalR. That keeps the phone's work light.
The trade-off is that it needs a live connection, which is acceptable for a game that is played online anyway. If
mobile reconnects become a problem, action pages can use static server rendering with plain form posts.

## Solution layout

```
Spellbound.slnx
src/
  Spellbound.Domain/        pure game rules (no dependencies); for now only Round, which the host uses
  Spellbound.Web/           Blazor app
    Hosting/                the reusable game host: accounts, the database (HostDatabase, SQL scripts in Sql/), rounds,
                            admin page, bans, backups,
                            rate limits, multi-account signals (namespace Spellbound.Web.Hosting)
    Components/Account/     sign-in and account pages (part of the host, not yet moved into Hosting/)
    PlaceholderGame.cs      the game's place until the new design lands: answers the host's hooks (below)
    Components/Pages/Home   the landing page
    Data/Sql/               the game's SQL scripts (009 dropped the old game's tables; the next game starts at 010)
    wwwroot/fonts/          the theme's fonts, self-hosted, with their licences (OFL-*.txt); see theme.md
tools/
  Spellbound.Playtest/      plays every page in a phone-sized browser (Playwright) against a fresh app and database,
                            with screenshots and a report (.claude/skills/playtest)
tests/
  Spellbound.Domain.Tests/  xUnit tests for the rules (for now, Round's)
  Spellbound.Web.Tests/     integration tests: real app on a real PostgreSQL, a fresh database per test
docs/                       architecture, roadmap, theme, deploy
scripts/bootstrap.ps1       one-time project generation
```

### The game host boundary

Everything a game server needs that isn't about Spellbound lives in `Hosting/`, so it can become its own project
(a Razor class library) when a second game is planned. Until then it stays a folder: one project is simpler, and
extension points designed for a single game tend to come out wrong.

- **Hosting never depends on the game.** It may use `Round` from the domain (the round itself knows nothing about
  Spellbound), and nothing else from `Spellbound.Domain` or the game's web code. `HostingBoundaryTests` enforces this
  on both the compiled types and the source files.
- **The game plugs in** through `Hosting/GameHooks.cs`: `IGameSchema` (the game's own SQL scripts, migrated after
  the host's; hooks get the host's `DbSession`, so both save in one transaction), `IRoundLifecycle` (the game the server always keeps running,
  `AlwaysOnMode` and `NextAlwaysOn()`: its mode, length and rules; and `OnGameEndedAsync`, what happens when a game ends,
  inside the reset's transaction) and `IPlayerSummaries` (how a player shows on the admin page), and
  `IAccountLifecycle` (deleting an account deletes the player's game data in the same transaction, and the data
  download includes it). `AdminPageOptions.GameSections` adds game sections to the admin page.
  `PlaceholderGame` implements the hooks for now: it keeps a round going and stores nothing about players.
- `Program.cs` calls `AddGameHost()`, `UseGameHostAsync()` and `MapGameHost()` for the host, then registers the game.
- **Tables** are split the same way: `Hosting/Sql` creates `accounts`, `games`, `bans`, `account_networks` and
  `server_secrets`; `Data/Sql` holds the game's scripts. The old game's tables are gone (`008` dropped Skirmish's,
  `009` the Realm's); the next game's scripts start at `010`.
- **When a second game comes:** move `Hosting/` and `Components/Account/` into a `GameHost` Razor class library,
  register its assembly with the router (`AdditionalAssemblies`), and split the tests. Roughly 2–3 days.

### Many games at once (M5)

A server runs **many games**: the host's `games` table (`HostedGame`: id, mode, round number, start, end, `ended_at`
once wrapped up, and the rules as jsonb the host never reads). The server always keeps one running (`AlwaysOnMode`),
numbered per mode; `PlaceholderGame` runs mode `placeholder` with no rules (`{}`). Bans are global (per account).

## Persistence notes

- **Changing a table** needs a new SQL script: `Hosting/Sql/NNN_name.sql` for the host's tables, `Data/Sql/NNN_name.sql`
  for the game's, numbered one higher than the last (they're embedded in the app). `SchemaMigrator` runs the new ones
  at startup, each in a transaction, records them in `schema_versions`, and holds an advisory lock so two instances
  can't both run them. Never edit a released script. The tests run every script on a fresh database, so a broken one
  fails CI.
- **Data access** is SQL through `HostDatabase` and `DbSession` (`Hosting/Database.cs`): one transaction per call,
  positional parameters (`$1`, `$2`), a `Jsonb` wrapper for jsonb values. Row locks (`SELECT … FOR UPDATE`) with a
  5-second `lock_timeout` are how the old game made a double tap wait for the first one.
- **Times** are stored as UTC (`timestamptz`); `DbSession` converts any `DateTimeOffset` with another offset.
  PostgreSQL keeps whole microseconds, so a time that must compare exactly after a round trip should be rounded to them.
- **Rounds:** each round is a row in `games`. `RoundKeeper`, a hosted `BackgroundService`, checks every minute: it
  creates round 1 on first start, and after a round ends it lets the game wrap it up (`OnGameEndedAsync`), marks the
  game ended and starts the next in one transaction, under a transaction advisory lock per mode, so two hosts can't
  both reset.
- **Notifications** (`Hosting/Push.cs`, `Hosting/WebPushCrypto.cs`): Web Push with .NET's own cryptography, no
  package. Players turn them on per device on the Account page (`wwwroot/push.js` subscribes with the server's VAPID
  key; `wwwroot/sw.js`, a service worker with no fetch handler, shows them). `push_subscriptions` holds each device's
  push address and keys. The game hands a `PushMessage` to `PushSink` after its transaction commits; `PushSender`, in
  the background, encrypts it for each of the player's devices (RFC 8291, tested against the RFC's own example) and
  posts it with a VAPID token (RFC 8292), forgetting devices the push service no longer knows. The VAPID key is made
  once and kept in `server_secrets`. For now only the Account page's test sends one.
- **Privacy** (`/privacy`, `Hosting/Privacy.cs`): a notice written from what the code stores, filled in from the
  `Privacy` config section (operator, contact, hosting; the app won't start outside Development without them). It
  promises things the code does, so keep them true together: network hashes are deleted after 30 days
  (`AccountSignals`), the app logs no IP addresses, deleting an account deletes its game data and log rows, and
  **Account → Download my data** (`/Account/Data`) gives everything stored about the player: the account row, the
  network hashes and, through `IAccountLifecycle.ExportAsync`, the game's data. Bans outlive a deleted account. The
  page is host code, but its "Your game" and "Other players" lines describe the game; they say "nothing yet" until
  the new game stores something.
- **Alerts** (`Hosting/Alerts.cs`): a logger provider sends every error-level log entry to `AlertSink`, which keeps
  the last 50 for the admin page's Problems section; with `Alerts:WebhookUrl` set, `AlertSender` pushes a short notice
  to an ntfy topic or a Discord webhook (told apart by host). A push names only the source and the exception type, never
  the message, and each kind is pushed at most once an hour (judged by when it happened). `BackupKeeper` logs an error
  when the newest backup is over 26 hours old, so a backup that silently stopped also alerts.
- **Abuse protection:** `RateLimits` (config section `RateLimits`) limits requests per IP: account form posts
  (10 per 5 minutes), opening the live game connection, and other non-static requests; static files are unlimited.
  `AccountSignals` stores, per account, keyed hashes of the networks it signed in from (never the IP itself; the key
  is in `server_secrets`), and the admin page
  lists networks shared by several accounts in the last 30 days, as hints, not automatic bans. Deleting an account
  deletes its signals.
- **Accounts** (`Hosting/Accounts.cs`, `Hosting/AccountEndpoints.cs`): players sign in with Google, so there are no
  passwords, resets or lockouts, and the server sends no email. Google's answer lands in a short-lived external cookie;
  `/Account/SignedIn` finds or creates the `accounts` row and issues the sign-in cookie with our own account id (the
  owner id everywhere), the email as the name, and nothing from Google beyond that. The id is a hash of the provider
  and Google's id, so it is stable: signing in again after deleting an account gets the same id, and a ban holds.
  In Development, `/Account/DevSignIn` signs in with just an email (local runs and tests); it isn't mapped elsewhere,
  and outside Development the app won't start without `Authentication:Google:ClientId` and `ClientSecret`.
  Deleting an account (Account page) deletes its network hashes and, through `IAccountLifecycle`, its game data in
  one transaction with the account.
- **Admin:** the `Admin` policy allows accounts whose email is in the `Admins` config list; `AdminHandler` reads the
  list on every check. So admin access rests on the email Google reports for the account. Google verifies the
  addresses of its own accounts; if that ever isn't enough (some Workspace setups), check the `email_verified` claim
  in `/Account/SignedIn` and refuse unverified ones. The `bans` table holds banned players; the game gets them in `OnGameEndedAsync` to leave
  them out of anything public. A banned account can't sign in, and an open sign-in is checked against the database at most once a minute (open game connections every five
  minutes), so a ban or a deletion signs the player out.
- **Tests** need a PostgreSQL server: `SPELLBOUND_TEST_POSTGRES` (default `Host=localhost;Username=postgres;
  Password=postgres`, as in CI and `docker-compose.dev.yml`). `TestDatabase` migrates a template database once per run
  and copies it for each test.

## Hosting plan (M3)

- One small VPS (for example Hetzner, EU) running Docker Compose with the app and **Caddy** for automatic HTTPS
  and PostgreSQL (`Dockerfile`, `docker-compose.yml`, `Caddyfile`; steps in [deploy.md](deploy.md)). The database has
  its own volume; the app's `/data` volume holds the backups and the Data Protection keys
  (`DataProtection:KeysDirectory`), so restarts and new images keep sign-ins.
  Only Caddy is published; the app trusts its `X-Forwarded-*` headers (`ForwardedHeaders:Enabled`) so it sees HTTPS.
  The image runs as the non-root `app` user. `/healthz` reports health.
- **Backups:** `BackupKeeper`, a hosted service, runs `pg_dump` once a night (`Backup:Directory`, on in the Docker
  image) and keeps the newest few. `pg_dump` must be the server's major version or newer, so the image's client (16)
  and the compose file's `postgres:16` move together. `Backup:RestoreFrom` runs `pg_restore` at startup, but only into
  a database no migration has run on, and before the startup migration. Copying the files off the server is a host
  cron job (see [deploy.md](deploy.md#backups)).
- **Cloudflare** free tier in front for DNS, DDoS protection and static asset caching.
- **PWA manifest and service worker**, so the game installs to the home screen without an app store.
- Global events (round end and reset) run in the `RoundKeeper` hosted service.
  Players never need a per-player job.
- CI: GitHub Actions builds and tests on every push, builds the Docker image and checks that it starts
  (`.github/workflows/ci.yml`).
