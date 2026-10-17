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
  Spellbound.Web/           Blazor app: pages, Identity, RavenDB setup, GameService, config
tests/
  Spellbound.Domain.Tests/  xUnit tests for the rules
  Spellbound.Web.Tests/     integration tests: real app, in-memory RavenDB (TestDriver)
docs/                       design, architecture, roadmap
scripts/bootstrap.ps1       one-time project generation
```

M1 has no separate Infrastructure project, because a single web project is easier to work with. Split out
`Spellbound.Infrastructure` once a second host appears, such as a worker for round resets.

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
  properties are treated as document keys (`RavenSetup.IsDocumentKey`), so `Mage.Id` (a Guid) is plain data.
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
- **Identity:** RavenDB.Identity does not save changes itself, so `SavingUserManager` saves after every create,
  update and delete. It finds users by email first because its by-name lookup is an index query. RavenDB.Identity
  has no passkey support, so the template's passkey pages were removed.
- **Licensing:** the embedded server runs without a license file in development and tests. Check RavenDB's license terms
  and which edition production needs before the M3 deployment.

## Hosting plan (M3)

- One small VPS (for example Hetzner, EU) running Docker Compose with the app and **Caddy** for automatic HTTPS. The
  `RavenData` directory lives on a mounted volume.
- Nightly off-site backup with a RavenDB periodic backup task. Check which backup destinations the license tier allows.
- **Cloudflare** free tier in front for DNS, DDoS protection and static asset caching.
- **PWA manifest and service worker**, so the game installs to the home screen without an app store.
- Global events (round end and reset, hall of fame snapshot) run in the `RoundKeeper` hosted service.
  Players never need a per-player job.
- CI: GitHub Actions builds and tests on every push (`.github/workflows/ci.yml`).
