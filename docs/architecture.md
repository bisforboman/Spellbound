# Spellbound architecture

## Stack

- **.NET 10**, with **Blazor Server** (interactive server rendering) for the UI, all in C#
- **ASP.NET Core Identity** (individual accounts) for sign-in, from the official Blazor template
- **RavenDB** (embedded in the web process via `RavenDB.Embedded`) for game data and Identity users
  (`RavenDB.Identity`). Move to a separate RavenDB server if load or a second host requires it
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
    Hosting/                the reusable game host: accounts, RavenDB setup, rounds, admin page, bans, backups,
                            rate limits, multi-account signals (namespace Spellbound.Web.Hosting)
    Components/Account/     the template's Identity pages (part of the host, not yet moved into Hosting/)
    Components/Game/, GameService, SpellboundHost, Data/   the game itself
tests/
  Spellbound.Domain.Tests/  xUnit tests for the rules
  Spellbound.Web.Tests/     integration tests: real app, in-memory RavenDB (TestDriver)
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
- **The game plugs in** through `Hosting/GameHooks.cs`: `IRoundLifecycle` (round length, and what happens when a round
  ends, inside the reset's transaction; Spellbound records its hall of fame) and `IPlayerSummaries` (how a player shows
  on the admin page), and `IAccountLifecycle` (deleting an account deletes the player's game data in the same
  transaction; Spellbound removes their mages and news from every round). `AdminPageOptions.GameSections` adds game sections to the admin page (Spellbound's rules table).
  `SpellboundHost` implements the hooks.
- `Program.cs` calls `AddGameHost()`, `UseGameHostAsync()` and `MapGameHost()` for the host, then registers the game.
- **Database keys** are split the same way: `HostStore` (conventions, licence, `rounds/current`, `admin/*`,
  `signals/*`) and `GameKeys` (`mages/{round}/…`, `news/{round}/…`, `halloffame/…`).
- **When a second game comes:** move `Hosting/` and `Components/Account/` into a `GameHost` Razor class library,
  register its assembly with the router (`AdditionalAssemblies`), and split the tests. Roughly 2–3 days.

## Request flow for a game action

```
page form → GameService.ExploreAsync(userId, turns)
  1. open a session (one per action: a Blazor Server circuit outlives one action) with optimistic
     concurrency on, load the current round (rounds/current) and refuse if it has ended,
     then load the mage by key: mages/{round}/{ownerId}
  2. report = mage.Explore(turns, timeProvider.GetUtcNow(), rules)
  3. if report.Succeeded: add the report to the news log document (news/{round}/{ownerId})
  4. SaveChangesAsync() saves both in one transaction. On ConcurrencyException, tell the player to try again
  5. render the report
```

**Attacks and hexes** change two mages. `GameService.HostileAsync` loads the attacker by key, finds the target's owner
with a query on the mage's public `Id` (a Guid; owner ids are account keys and never go in URLs), then loads the
target by key. Both mages and both news logs are saved in one transaction with optimistic concurrency, so if either
player acts at the same moment, nothing happens and the attacker is asked to retry. The domain gets an
`IRandomSource` (`SharedRandom` in the app, `SeededRandom` in tests).

- `TimeProvider` is registered in DI (`TimeProvider.System`) so tests can fake time.
- `GameRules` is bound from `appsettings.json` → `"GameRules"` and injected as a singleton.
- The embedded RavenDB server starts on first use of `IDocumentStore`; data lives in `RavenData/` (gitignored).

## Turns: lazy regeneration

The database stores `Turns` and `TurnsAccruedAt`. `TurnClock.Accrue` turns elapsed time into whole turns and keeps
the leftover partial interval. When the mage is at the cap, `TurnsAccruedAt` is moved to "now", so time at the cap is lost.
No scheduler is needed. The overview page computes "next turn in mm:ss" the same way.

## Persistence notes

- Documents need no schema or migrations. Adding a property to `Mage` just works; renaming one needs a patch script.
- `Mage` is stored as-is (RavenDB sets private setters and the private `_activeEffects` list). Only string `Id`
  properties are treated as document keys (`HostStore.IsDocumentKey`), so `Mage.Id` (a Guid) is plain data.
- **Load by key, never query, on the action path.** Loads are always consistent; queries go through indexes that
  update a moment after each write. The one exception is finding an attack target's owner by mage id, which never
  changes; the target itself is then loaded by key.
- **Rankings** stream the round's mages by key prefix (`mages/{round}/`), which is consistent and needs no index, and
  sort in memory. That's fine for one server's players; add a static index with a stored networth if it grows past a
  few thousand.
- **Alerts:** `NewsLog.Unseen` counts hostile entries. The overview reads it and resets it with a patch, so the reset
  can't overwrite news that another mage writes at the same moment.
- **One mage per player per round** comes from the key: creating `mages/{round}/{ownerId}` with an empty change vector
  fails if it exists. Old rounds' mages and news stay in the database; nothing reads them after the reset.
- **Rounds:** `rounds/current` holds the round being played. `RoundKeeper`, a hosted `BackgroundService`, checks it
  every minute: it creates round 1 on first start, and after a round ends it stores `halloffame/{round}` and moves
  `rounds/current` to the next round in one transaction. Optimistic concurrency means two hosts can't both reset.
- **Abuse protection:** `RateLimits` (config section `RateLimits`) limits requests per IP: account form posts
  (10 per 5 minutes), opening the live game connection, and other non-static requests; static files are unlimited.
  Login locks an account for 5 minutes after 5 wrong passwords. `AccountSignals` stores, per account, keyed hashes of
  the networks it signed up or logged in from (never the IP itself; the key is `admin/secrets`), and the admin page
  lists networks shared by several accounts in the last 30 days, as hints, not automatic bans. Deleting an account
  deletes its signals.
- **Email:** none yet (`IdentityNoOpEmailSender`). "Forgot password" tells players to ask an admin, who gives them a
  temporary password from the admin page (`AdminService.ResetPasswordAsync`). Changing an email address needs a
  confirmation link, so it doesn't work until real email is added.
- **Admin:** the `Admin` policy allows accounts whose email is in the `Admins` config list; `AdminHandler` reads the
  list on every check. `admin/bans` holds the ban list. `GameService` checks it on every action and filters banned
  mages out of rankings and targeting; `RoundKeeper` leaves them out of the hall of fame. A ban also locks the account
  (lockout until the end of time) and changes its security stamp, so open sessions are signed out when they revalidate.
- **Identity:** RavenDB.Identity does not save changes itself, so `SavingUserManager` saves after every create,
  update and delete. It finds users by email first because its by-name lookup is an index query. RavenDB.Identity
  has no passkey support, so the template's passkey pages were removed.
- **Licensing:** production uses a free RavenDB **Community** licence key, set as `RavenDb__License` (or `RAVEN_License`)
  to the licence JSON. Outside Development the app won't start without one, and RavenDB refuses an invalid one. Without
  a key the server software falls under the AGPLv3 instead (see the EULA in the `RavenDB.Embedded` package). A Developer
  key is not allowed in production. Community keys last a year and require the latest RavenDB major version, so renew
  the key and update the packages each year. Development and tests run without a key.

## Hosting plan (M3)

- One small VPS (for example Hetzner, EU) running Docker Compose with the app and **Caddy** for automatic HTTPS
  (`Dockerfile`, `docker-compose.yml`, `Caddyfile`; steps in [deploy.md](deploy.md)). The app's `/data` volume holds
  the database and the Data Protection keys (`DataProtection:KeysDirectory`), so restarts and new images keep sign-ins.
  Only Caddy is published; the app trusts its `X-Forwarded-*` headers (`ForwardedHeaders:Enabled`) so it sees HTTPS.
  The image runs as the non-root `app` user. `/healthz` reports health.
- **Backups:** `BackupKeeper`, a hosted service, exports the database once a night (`Backup:Directory`, on in the Docker
  image) with RavenDB's export, which works on every licence, unlike the periodic backup task. It keeps the newest few.
  `Backup:RestoreFrom` imports a dump at startup, but only into an empty database. Copying the files off the server is a
  host cron job (see [deploy.md](deploy.md#backups)).
- **Cloudflare** free tier in front for DNS, DDoS protection and static asset caching.
- **PWA manifest and service worker**, so the game installs to the home screen without an app store.
- Global events (round end and reset, hall of fame snapshot) run in the `RoundKeeper` hosted service.
  Players never need a per-player job.
- CI: GitHub Actions builds and tests on every push, builds the Docker image and checks that it starts
  (`.github/workflows/ci.yml`).
