# Spellbound architecture

## Stack

- **.NET 10**, with **Blazor Server** (interactive server rendering) for the UI, all in C#
- **ASP.NET Core Identity** (individual accounts) for sign-in, from the official Blazor template
- **EF Core + SQLite** for M1–M3. Move to PostgreSQL only if load or features require it
- **xUnit** for domain tests, plus `WebApplicationFactory` integration tests for the web app

Blazor Server keeps state on the server and sends small UI diffs over SignalR. That keeps the phone's work light.
The trade-off is that it needs a live connection, which is acceptable for a game that is played online anyway. If
mobile reconnects become a problem, switch the action pages to static server rendering with plain form posts. The
domain code doesn't change.

## Solution layout

```
Spellbound.slnx
src/
  Spellbound.Domain/        pure game rules: Mage, TurnClock, GameRules, SpellBook (no dependencies)
  Spellbound.Web/           Blazor app: pages, Identity, EF Core DbContext + migrations, config
tests/
  Spellbound.Domain.Tests/  xUnit tests for the rules
  Spellbound.Web.Tests/     integration tests: real app, private in-memory SQLite
docs/                       design, architecture, roadmap
scripts/bootstrap.ps1       one-time project generation
```

M1 has no separate Infrastructure project, because a single web project is easier to work with. Split out
`Spellbound.Infrastructure` once a second host appears, such as a worker for round resets.

## Request flow for a game action

```
page form → GameService.ExploreAsync(userId, turns)
  1. load Mage (tracked) for the current round and user, in a fresh DbContext from IDbContextFactory
     (a Blazor Server circuit outlives one action, so a scoped context would hold stale entities)
  2. report = mage.Explore(turns, timeProvider.GetUtcNow(), rules)
  3. if report.Succeeded: save the report to the news log
  4. SaveChangesAsync(). On DbUpdateConcurrencyException, reload and tell the player to try again
  5. render the report
```

- `TimeProvider` is registered in DI (`TimeProvider.System`) so tests can fake time.
- `GameRules` is bound from `appsettings.json` → `"GameRules"` and injected as a singleton.
- EF migrations are applied at startup (`Database.Migrate()` in `Program.cs`).

## Turns: lazy regeneration

The database stores `Turns` and `TurnsAccruedAt`. `TurnClock.Accrue` turns elapsed time into whole turns and keeps
the leftover partial interval. When the mage is at the cap, `TurnsAccruedAt` is moved to "now", so time at the cap is lost.
No scheduler is needed. The overview page computes "next turn in mm:ss" the same way.

## Persistence notes

- Map `Mage` with EF Core's private-setter support. `ActiveEffects` is an owned collection
  (`OwnsMany(m => m.ActiveEffects)`) backed by the private `_activeEffects` field.
- **Concurrency:** SQLite has no `rowversion`. Add a `long Version` shadow property or a column marked
  `IsConcurrencyToken()`, and increment it in `SaveChanges` for modified mages.
- **`DateTimeOffset` on SQLite** is stored as text and can't be compared in SQL. Store UTC and use the built-in
  `DateTimeOffsetToBinaryConverter` if queries ever need to filter on it.
- Unique index on `(OwnerId)` for M1, since each user has one mage. This becomes `(RoundId, OwnerId)` in M3.

## Hosting plan (M3)

- One small VPS (for example Hetzner, EU) running Docker Compose with the app and **Caddy** for automatic HTTPS. The
  SQLite file lives on a mounted volume.
- Nightly off-site backup of the database file (SQLite `.backup` or Litestream).
- **Cloudflare** free tier in front for DNS, DDoS protection and static asset caching.
- **PWA manifest and service worker**, so the game installs to the home screen without an app store.
- Global events (round end and reset, rankings snapshot) run as a hosted `BackgroundService` or a cron job.
  Players never need a per-player job.
- CI: GitHub Actions builds and tests on every push (`.github/workflows/ci.yml`).
