# Spellbound architecture

## Stack

- **.NET 10**, with **Blazor Server** (interactive server rendering) for the UI, all in C#
- **Sign-in with Google** (`Microsoft.AspNetCore.Authentication.Google`) and a cookie; no passwords are stored
- **PostgreSQL 16** through plain **Npgsql** (SQL, no ORM) for game data and accounts. It runs
  in its own container next to the app
- **xUnit** for domain tests, plus `WebApplicationFactory` integration tests for the web app

Blazor Server keeps state on the server and sends small UI diffs over SignalR. That keeps the phone's work light.
The trade-off is that it needs a live connection, which is acceptable for a game that is played online anyway. If
mobile reconnects become a problem, switch the action pages to static server rendering with plain form posts. The
domain code doesn't change.

## Solution layout

```
Spellbound.slnx
src/
  Spellbound.Domain/        pure game rules: Mage, TurnClock, GameRules, SpellBook, UnitBook (no dependencies)
  Spellbound.Web/           Blazor app
    Hosting/                the reusable game host: accounts, the database (HostDatabase, SQL scripts in Sql/), rounds,
                            admin page, bans, backups,
                            rate limits, multi-account signals (namespace Spellbound.Web.Hosting)
    Components/Account/     sign-in and account pages (part of the host, not yet moved into Hosting/)
    Components/Game/, GameService, SpellboundHost, Data/   the game itself (Data/Sql: its SQL scripts)
tools/
  Spellbound.Balance/       balance simulation: bots play the domain rules for a round (docs/balance.md)
  Spellbound.Playtest/      plays every page in a phone-sized browser (Playwright) against a fresh app and database,
                            with screenshots and a report (.claude/skills/playtest)
tests/
  Spellbound.Domain.Tests/  xUnit tests for the rules
  Spellbound.Web.Tests/     integration tests: real app on a real PostgreSQL, a fresh database per test
docs/                       design, architecture, roadmap
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
  inside the reset's transaction; Spellbound records its hall of fame) and `IPlayerSummaries` (how a player shows
  on the admin page), and `IAccountLifecycle` (deleting an account deletes the player's game data in the same
  transaction; Spellbound removes their mages and news from every round). `AdminPageOptions.GameSections` adds game sections to the admin page (Spellbound's rules table).
  `SpellboundHost` implements the hooks.
- `Program.cs` calls `AddGameHost()`, `UseGameHostAsync()` and `MapGameHost()` for the host, then registers the game.
- **Tables** are split the same way: `Hosting/Sql` creates `accounts`, `games`, `bans`, `account_networks` and
  `server_secrets`; `Data/Sql` (through `SpellboundSchema`) creates `mages`, `news`, `hall_of_fame` and `actions`,
  keyed by game id.
- **When a second game comes:** move `Hosting/` and `Components/Account/` into a `GameHost` Razor class library,
  register its assembly with the router (`AdditionalAssemblies`), and split the tests. Roughly 2–3 days.

### Many games at once (M5)

Realm and Skirmish are two modes of one game, not two games, so `Hosting/` stays one host. A server runs **many
games**: the host's `games` table (`HostedGame`: id, mode, round number, start, end, `ended_at` once wrapped up, and the
rules as jsonb the host never reads) replaced the single `current_round` row. Each Realm round is one game: the
server always keeps one running (`AlwaysOnMode`), numbered per mode. Every game stores its own rules
(`GameData.RulesOf`), so `GameRules` from configuration is the preset for the next round, not the set every action
uses; the action log's rules hash records which rules an action ran under. Rounds migrated from before games have no
stored rules (`{}`) and use the configured ones, as they did. Bans stay global (per account); the hall of fame is per
game. The Games page (`/`) lists the player's games; the Realm's pages (`/realm`, `/explore`, …) mean "the running
Realm round". Not built yet (part 2, with Skirmish): members, creating and joining games, and page addresses that name
a game.

## Request flow for a game action

```
page form → GameService.ExploreAsync(userId, turns)
  1. begin a transaction (one per action: a Blazor Server circuit outlives one action), load the running Realm game
     (its round and rules) and refuse if it has ended, then load the mage's row by key (game_id, ownerId) FOR UPDATE:
     a second tap waits here until the first commits, then sees the turns it left
  2. report = mage.Explore(turns, timeProvider.GetUtcNow(), the game's rules)
  3. if report.Succeeded: write the mage back into its row, and add the report to the news row (game_id, ownerId),
     which is locked too
  4. commit both. A row locked for more than 5 s (lock_timeout) or a deadlock tells the player to try again
  5. render the report
```

**Attacks and hexes** change two mages. `GameService.HostileAsync` locks the attacker (by key) and the target (by its
public `Id`, a Guid in the indexed `mage_id` column; owner ids are account keys and never go in URLs) in one
statement, ordered by owner id, then both news rows in the same order. So two mages attacking each other at once wait
for each other instead of deadlocking, and every acre taken arrives on the other side (`PvpFlowTests` checks both). The domain gets an
`IRandomSource` (`SharedRandom` in the app, `SeededRandom` in tests).

- `TimeProvider` is registered in DI (`TimeProvider.System`) so tests can fake time.
- `GameRules` is bound from `appsettings.json` → `"GameRules"` and injected as a singleton: the preset a new round
  copies into its `games` row. Actions use the round's own copy.
- The database is `ConnectionStrings:Spellbound`. At startup the app runs any new SQL scripts (`UseGameHostAsync`).
  In Development it connects to `localhost:5432` (start one with `docker compose -f docker-compose.dev.yml up -d`).

## Turns: lazy regeneration

The database stores `Turns` and `TurnsAccruedAt`. `TurnClock.Accrue` turns elapsed time into whole turns and keeps
the leftover partial interval. When the mage is at the cap, `TurnsAccruedAt` is moved to "now", so time at the cap is lost.
No scheduler is needed. The overview page computes "next turn in mm:ss" the same way.

## Persistence notes

- **Domain objects are jsonb snapshots.** A `mages` row holds the round, owner id and mage id as columns, and the whole
  `Mage` in a jsonb `Data` column; news and halls of fame work the same way. `DomainJson` stores the private state
  (private setters and the `_army`, `_activeEffects` and `_recentLosses` fields) without serialization attributes in
  the domain, and creates objects with their private constructor. So **adding a property or field to `Mage` needs no
  migration**: old rows read it as its initializer's value (mages saved before `CreatedAt` existed read the minimum
  date, so they are protected by spent turns only). Renaming or reshaping one needs an upcaster (see the roadmap);
  `DomainJsonTests` checks that nothing is lost.
- **Changing a table** needs a new SQL script: `Hosting/Sql/NNN_name.sql` for the host's tables, `Data/Sql/NNN_name.sql`
  for the game's, numbered one higher than the last (they're embedded in the app). `SchemaMigrator` runs the new ones
  at startup, each in a transaction, records them in `schema_versions`, and holds an advisory lock so two instances
  can't both run them. Never edit a released script. The tests run every script on a fresh database, so a broken one
  fails CI.
- **Data access** is SQL through `HostDatabase` and `DbSession` (`Hosting/Database.cs`): one transaction per call,
  positional parameters (`$1`, `$2`), a `Jsonb` wrapper for jsonb values. The game's queries are in `Data/GameData.cs`.
- **Concurrency is row locks** (`SELECT … FOR UPDATE`) inside each action's transaction, with a 5-second
  `lock_timeout`. Locks are always taken mages first, then news, each in owner order, so they can't deadlock.
  `GameFlowTests` fires 25 taps at once at a mage with turns for 20 and checks exactly 20 succeed.
- **Rankings** read the round's mages and sort in memory. That's fine for one server's players; add a networth column
  if it grows past a few thousand.
- **Alerts:** `news.unseen` counts hostile entries. The overview reads it with the row locked and resets it, so an
  attack landing at the same moment waits and counts next time.
- **One mage per player per round** comes from the key (round, owner id): a second insert does nothing. Old
  rounds' mages and news stay in the database; nothing reads them after the reset.
- **Times** are stored as UTC (`timestamptz`); `DbSession` converts any `DateTimeOffset` with another offset.
  `GameService.Now` is rounded to whole microseconds, PostgreSQL's precision, so a logged time is exactly the time the
  domain saw.
- **Action log** (`actions`, `Data/ActionLog.cs`): every successful player action (create, explore, build, meditate,
  cast, recruit, attack, hex) writes one row in its own transaction, so the log holds exactly what happened. A row has
  the round, the player and the target's owner, the time, the action's name and inputs, the random numbers it used
  (`RecordingRandom` wraps the luck, so the domain doesn't change), the report lines (and the target's), the mage's
  main numbers afterwards (`state`, and `target_state` for the other mage in an attack or hex, for balance queries)
  and a hash of the `GameRules` in force. Failed actions change
  nothing and aren't logged; nor are alert resets, round resets or admin actions. With a mage as it was before an
  action, the same time, rules and draws reproduce the action exactly (`ActionLogTests` replays an attack and gets
  the same saved mages); a replay tool isn't built. A round's rules are stored with it (`games.rules`), and the hash
  identifies them. Deleting an account deletes its rows and clears it from others' attacks.
  The admin page looks up a mage's latest actions by name. Volume: a few rows per player per check-in, under 1 KB
  each; drop old rounds with `DELETE FROM actions WHERE game_id < N`, or partition by game if it ever matters.
- **Rounds:** each Realm round is a row in `games`. `RoundKeeper`, a hosted `BackgroundService`, checks every minute:
  it creates round 1 on first start, and after a round ends it adds the `hall_of_fame` row, marks the game ended and
  starts the next in one transaction, under a transaction advisory lock per mode, so two hosts can't both reset.
- **Privacy** (`/privacy`, `Hosting/Privacy.cs`): a notice written from what the code stores, filled in from the
  `Privacy` config section (operator, contact, hosting; the app won't start outside Development without them). It
  promises things the code does, so keep them true together: network hashes are deleted after 30 days
  (`AccountSignals`), the app logs no IP addresses, deleting an account deletes its game data and log rows, and
  **Account → Download my data** (`/Account/Data`) gives everything stored about the player: the account row, the
  network hashes and, through `IAccountLifecycle.ExportAsync`, the game's data. Bans outlive a deleted account, and
  other players' news and action rows keep the deleted mage's name and battle numbers. The page is host code, but its
  "Your game" and "Other players" lines describe Spellbound; a second game supplies its own.
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
  in `/Account/SignedIn` and refuse unverified ones. The `bans` table holds banned players. `GameService` checks it on every action and filters banned
  mages out of rankings and targeting; `RoundKeeper` leaves them out of the hall of fame. A banned account can't sign
  in, and an open sign-in is checked against the database at most once a minute (open game connections every five
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
- Global events (round end and reset, hall of fame snapshot) run in the `RoundKeeper` hosted service.
  Players never need a per-player job.
- CI: GitHub Actions builds and tests on every push, builds the Docker image and checks that it starts
  (`.github/workflows/ci.yml`).
