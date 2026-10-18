# Spellbound persistence: alternatives to EF Core

> **Decision (17 Oct 2026):** the owner chose plain Npgsql (option B), but with **sign-in through Google** instead of
> passwords. So there is no ASP.NET Identity store to write at all: a small `users` table and Microsoft's Google
> authentication handler replace it (see the [roadmap](../roadmap.md), M4). Below is the research as it was written,
> which recommended keeping EF mainly because of the Identity store that this decision removes.

Research date: 2026-10-17. Repo state: `4d0d37f` (just after "Move from RavenDB to PostgreSQL (EF Core + Npgsql)").
Read-only research; nothing in the repo was changed.

Conventions in this report: **Fact** = verified from a cited source or from this repo / the local SDK.
**Judgement** = my opinion or estimate. Where a page could not be reached (learn.microsoft.com, npgsql.org and
source.dot.net failed DNS from this sandbox; the GitHub API was blocked), I say "from my knowledge, not verified here".

---

## 1. The decision, framed correctly

EF Core currently gives Spellbound three things. My assessment of each, checked against the code:

| What EF does today | Where | How hard to replace without EF |
|---|---|---|
| **Identity stores** (`AddEntityFrameworkStores<HostDb>`) | `GameHostSetup.cs`, `HostUserManager.cs` | **Smaller than assumed** (see 1.1). ~20 abstract methods + 5 passkey methods + schema |
| **Migrations + "model changed without migration" CI check** | `Data/Migrations` (1 migration, ~1,200 generated lines), `UseGameHostAsync` | Easy: ordered `.sql` files and a `schema_versions` table (~50–80 lines) or DbUp. The CI drift check has no equivalent without a model (see 4.3) |
| **xmin concurrency** on `mages`, `news`, `current_round` | `IsRowVersion()` in `HostDb`/`SpellboundModel` | Easy: `UPDATE … WHERE key AND version = @v`, check the row count. Two-mage attack = two such updates in one `NpgsqlTransaction` |
| (also) LINQ queries, `ExecuteUpdate/Delete`, `IGameModel.Configure(ModelBuilder)` as the game hook, UTC converter | ~15 call sites in `GameService`, `RoundKeeper`, `AdminService`, `AccountSignals`, `SpellboundHost`, `HostUserManager` | Mechanical rewrite to SQL; the tables are tiny (7 host/game tables, jsonb blobs + keys) |

**The key framing (judgement):** the dependency count only drops if EF leaves *entirely*. "EF for Identity, raw
Npgsql/Dapper/Marten for the game" keeps all three EF packages (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`,
`Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`) and *adds* something. It changes
ergonomics, not the dependency story. So the real question is binary:

> **Is a hand-written (or community) Identity store worth it to remove EF?**

Everything else EF does here (migrations for ~7 small tables, xmin, a dozen queries) is cheap to replace, because
the design already avoids what EF is good at: game state is jsonb blobs keyed by real columns, not a relational graph.

### 1.1 Identity without EF is smaller than "IUserStore plus ~10 interfaces" (fact)

Reflection over the .NET 10.0.13 assemblies on this machine:

- `Microsoft.AspNetCore.Identity.UserStoreBase<TUser,TKey,TUserClaim,TUserLogin,TUserToken>` lives in
  **`Microsoft.Extensions.Identity.Stores.dll`, which ships in the ASP.NET Core shared framework**
  (`shared/Microsoft.AspNetCore.App/*/Microsoft.Extensions.Identity.Stores.dll`). No NuGet package needed, no EF.
- It implements **14 interfaces**: `IUserStore`, `IUserLoginStore`, `IUserClaimStore`, `IUserPasswordStore`,
  `IUserSecurityStampStore`, `IUserEmailStore`, `IUserLockoutStore`, `IUserPhoneNumberStore`, `IQueryableUserStore`,
  `IUserTwoFactorStore`, `IUserAuthenticationTokenStore`, `IUserAuthenticatorKeyStore`,
  `IUserTwoFactorRecoveryCodeStore`, `IDisposable`.
- It leaves **20 abstract methods**: `CreateAsync, UpdateAsync, DeleteAsync, FindByIdAsync, FindByNameAsync,
  FindByEmailAsync, Users, FindUserAsync, FindUserLoginAsync, Get/Add/Replace/RemoveClaims(4), GetUsersForClaimAsync,
  Add/Remove/GetLogins(3), FindTokenAsync, AddUserTokenAsync, RemoveUserTokenAsync`.
  Password hash, security stamp, lockout, 2FA flag, authenticator key and **recovery codes** are implemented by the
  base class (the last two on top of the token methods), so they come "for free" once tokens are stored.
- **Passkeys are not in the base class.** The EF `UserOnlyStore`/`UserStore` in 10.0.13 additionally implement
  `IUserPasskeyStore` (5 methods: `AddOrUpdatePasskeyAsync, GetPasskeysAsync, FindByPasskeyIdAsync,
  FindPasskeyAsync, RemovePasskeyAsync`) and `IProtectedUserStore`. Passkeys in EF also need Identity schema
  version 3 and a migration for the passkey table
  ([dotnet-guide](https://www.dotnet-guide.com/articles/aspnet-core/passkeys-common-mistakes/), secondary source;
  interface: [api.apireview.net](https://api.apireview.net/catalog/5d73ac1895633a5d3c85d106963a0e82)).
- Spellbound uses `IdentityUser` (string key) with no roles (`AddIdentityCore`, admin is an email list), so
  the five-parameter `UserStoreBase` is the right base; no role store is needed.
- `AdminService.cs:247` uses `db.Users` LINQ; `IQueryableUserStore.Users` can throw `NotSupportedException` if
  AdminService moves to SQL (nothing in the template's account pages touches `HostDb` directly: grep confirms only
  `UserManager`/`SignInManager` are used there).

**Estimate (judgement):** a `PgUserStore : UserStoreBase<…>, IUserPasskeyStore<…>` on raw Npgsql is ~400–550 lines
plus a ~70-line schema (users, user_claims, user_logins, user_tokens, user_passkeys). 1.5–3 days of hobby time
including tests. The one subtle part is `UpdateAsync`: it must check `ConcurrencyStamp`
(`UPDATE … WHERE id = @id AND concurrency_stamp = @old`, return `IdentityResult.Failed(ConcurrencyFailure)` on 0 rows)
and write a new stamp, exactly as the EF store does.

**Testing the store (fact + judgement):** Microsoft's shared store test suite
`Microsoft.AspNetCore.Identity.Specification.Tests` is **deprecated as legacy; last real release 5.0.17 (May 2022)**
([NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.Specification.Tests)). The source still lives
in the dotnet/aspnetcore repo (from my knowledge), so the tests could be copied in, but there is no maintained package.
In practice the existing `SecurityFlowTests`/`AdminFlowTests` (real app, real Postgres) plus a focused store test class
(lockout, stamp change, 2FA enable + recovery code redeem once, external login round-trip, concurrency failure) is
the realistic safety net.

**Security risk (judgement):** the crypto (password hashing, TOTP, recovery-code comparison, token protection) stays in
Microsoft's `UserManager`/`SignInManager`/`UserStoreBase`; a custom store only persists strings. The real risks are
(a) losing a field (e.g. forgetting `LockoutEnd` or `SecurityStamp` on update → lockout or forced sign-out silently
breaks), (b) normalized-name lookups not using the normalized columns, (c) missing the concurrency stamp check. All are
testable. Note the project already ran on a community store (`RavenDB.Identity` 9.0.1 plus a `SavingUserManager`, see
commit `36df32e`), so this class of risk has been accepted before.

---

## 2. What others in .NET use (facts with sources)

### 2.1 Libraries: status as of Oct 2026

| Library | Latest / date | .NET 10 | Licence | Notes |
|---|---|---|---|---|
| **Npgsql** (ADO.NET) | 10.0.3, 2026-05-27 | yes | PostgreSQL | Already approved. [NuGet](https://www.nuget.org/packages/Npgsql) |
| **Dapper** | 2.1.89, 2026-09-23 | net8 + net10 targets | Apache-2.0 | ~800M downloads, actively maintained. [NuGet](https://www.nuget.org/packages/Dapper) |
| **Marten** | 9.50.3, 2026-10-15 | yes | MIT | Deps: JasperFx, JasperFx.Events, Weasel.Postgresql, Weasel.Storage, RecyclableMemoryStream. [NuGet](https://www.nuget.org/packages/Marten) |
| **Wolverine** (WolverineFx) | 6.51.0, 2026-10-15 | yes | MIT | Messaging/mediator; pairs with Marten. [NuGet](https://www.nuget.org/packages/WolverineFx) |
| **linq2db** | 6.5.0, 2026-09-11 | yes | MIT | Typed LINQ → SQL, no change tracking. [NuGet](https://www.nuget.org/packages/linq2db) |
| **RepoDB** | 1.16.0, 2026-09-09 (1.17 beta Oct) | compatible | Apache-2.0 | Alive. [NuGet](https://www.nuget.org/packages/RepoDb) |
| **SqlKata** | 4.0.1, 2025-02-01 | compatible | MIT | Query builder; slow cadence. [NuGet](https://www.nuget.org/packages/SqlKata) |
| **YesSql** | 6.0.0, 2026-06-23 | yes | MIT | Document DB over SQL; Orchard Core's store. [NuGet](https://www.nuget.org/packages/YesSql) |
| **DbUp** (dbup-postgresql) | 7.0.1, 2026-02-23 | net8 target | MIT | SQL-script migrations. [NuGet](https://www.nuget.org/packages/dbup-postgresql) |
| **FluentMigrator** | 8.0.1, 2026-01-21 | compatible | Apache-2.0 | C# migrations. [NuGet](https://www.nuget.org/packages/FluentMigrator) |
| **grate** | 2.1.6, 2026-07-29 | (dotnet tool) | MIT | RoundhousE successor, CLI tool. [NuGet](https://www.nuget.org/packages/grate) |
| **Evolve** | 3.2.0 stable, **2023-06-30** (3.4.0-alpha1 2025-09) | compatible | MIT | **Effectively stalled**; avoid. [NuGet](https://www.nuget.org/packages/Evolve) |
| **KurrentDB** (ex-EventStoreDB) | 25.x | n/a (server) | **Kurrent License v1, not OSI open source**; enterprise features need a key | [Kurrent 25.0 release](https://www.kurrent.io/releases/kurrentdb/25-0/), [ESLv2 history](https://www.kurrent.io/blog/introducing-event-store-license-v2-eslv2) |
| **Memstate** (prevalence) | last push 2023-11 | netstandard2.0 | — | Successor of OrigoDB; dormant. [ecosyste.ms](https://awesome.ecosyste.ms/projects/github.com%2FDevrexLabs%2Fmemstate), [OrigoDB](https://github.com/devrexlabs/origodb) |

**Licence weather (fact):** AutoMapper and MediatR moved to dual/commercial licences under Lucky Penny Software
([Bogard](https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/)); MassTransit v9 is
commercial under Massient, v8 stays open source
([announcement](https://masstransit.io/introduction/v9-announcement)); EventStoreDB became source-available (above).
Marten/Wolverine remain MIT; JasperFx sells support and a commercial add-on (CritterWatch) around them (from my
knowledge; I could not confirm current CritterWatch terms). **Judgement:** a JasperFx relicensing is the main
"MassTransit-style" risk among the candidates, because Marten is company-backed with a growing commercial layer; Dapper,
Npgsql, linq2db and DbUp are community/foundation-style and low-risk.

### 2.2 Identity stores outside EF (facts)

| Package | Status | 2FA / recovery codes | Passkeys |
|---|---|---|---|
| [Marten.AspNetIdentity](https://www.nuget.org/packages/Marten.AspNetIdentity) | 9.0.26, 2026-05-21, ~30k total downloads, net8/9/10, needs Marten ≥ 8.37; **licence not stated on NuGet** | Identity.Stores-based (likely, unverified) | not stated |
| [Innovorium.AspNetCore.Identity.Marten](https://www.nuget.org/packages/Innovorium.AspNetCore.Identity.Marten) | 0.1.0, 2026-07-30, **~200 downloads**, MIT, net10 | yes (authenticator keys, recovery codes) | **yes** |
| Others on Marten ([Marten.AspNetCore.Identity](https://packages.nuget.org/packages/Marten.AspNetCore.Identity), [.Updated](https://packages.nuget.org/packages/Marten.AspNetCore.Identity.Updated), [AndreGoepel.Marten.Identity](https://www.nuget.org/packages/AndreGoepel.Marten.Identity)) | small, single-maintainer | varies | varies |
| [AdaskoTheBeAsT.Identity.Dapper](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Identity.Dapper) | 4.0.0, Apache-2.0, source-generated SQL, Postgres provider; 13 stars; you create the schema | tokens yes (so recovery codes work); 2FA/authenticator not documented | not mentioned |
| [terzano/Identity.Dapper](https://github.com/terzano/Identity.Dapper), Hymma.DapperIdentity | old / SQL Server only | — | — |
| [linq2db.Identity](https://www.nuget.org/packages/linq2db.Identity) | 6.0.0, 2025-12-06, netstandard2.0 | probably (Stores-based) | unlikely for netstandard2.0 |

**Judgement:** every non-EF Identity store is a small single-maintainer project, and none is clearly ahead of what
you get by subclassing `UserStoreBase` yourself. If EF goes, **write the store, don't adopt one**: it's Microsoft's base
class, zero new dependencies, and it's ~500 lines you fully understand. That is the opposite of the RavenDB.Identity
experience, where the store's save semantics needed a `SavingUserManager` workaround.

### 2.3 What other .NET business apps do (facts, secondary sources flagged)

*These are business apps, not games. For actual comparable games see section 6.*

- **Bitwarden server:** Dapper + stored procedures for SQL Server, EF Core for Postgres/MySQL/SQLite, behind shared
  repository interfaces; DbUp for MSSQL migrations, EF for the others
  ([Factory.ai wiki](https://factory.ai/open-source-wikis/bitwarden-server), auto-generated secondary source).
- **nopCommerce:** abandoned EF Core for **linq2db + FluentMigrator** since 4.30
  ([docs](https://docs.nopcommerce.com/en/developer/tutorials/migrations.html)).
- **Orchard Core:** **YesSql** (document storage over relational DBs). From my knowledge, its Identity is its own user
  store over YesSql, not ASP.NET Identity's EF stores.
- **Jellyfin:** went the *other* way: raw SQLite → **EF Core** in 10.11 (2025) to centralize data access; the
  upgrade migration was painful for users ([alternativeto](https://alternativeto.net/news/2025/10/jellyfin-10-11-migrates-to-ef-core-boosts-speed-drops-32-bit-arm-support),
  [jellywatch](https://jellywatch.app/blog/jellyfin-10-11-ef-core-migration-chaos-lessons-learned-2026), community source).
- **Stack Overflow:** Dapper's origin (well known, not re-verified here).
- **Event-sourced .NET apps / "Critter Stack" users:** Marten (+ Wolverine) on Postgres; JasperFx is extending the
  model to SQL Server ("Polecat") and SQLite ("Fisher")
  ([podcast, Oct 2026](https://www.spreaker.com/episode/critter-stack-update-with-jeremy-miller--75608463)).

**Pattern (judgement):** the mainstream answer for "ASP.NET Identity + Postgres" is still EF Core. Teams that leave
EF go to Dapper/linq2db plus a script migrator, and either keep EF *only* for Identity or own their user store. Small
hobby game servers I'm aware of mostly use EF Core or Dapper; nothing game-specific stands out.

---

## 3. Options in depth

Each option: what changes, then Identity / migrations / concurrency / backups / tests / Hosting boundary / startup /
dependencies. Sizes are judgement.

### Option A: Keep EF Core (status quo), tidy the edges

- **Changes:** none required. Possible tidy-ups: restore the template's passkey pages (EF stores already implement
  `IUserPasskeyStore`; needs `SchemaVersion = Version3` + a migration); keep game code on `ExecuteUpdate`/raw SQL
  where LINQ is awkward.
- **Identity:** Microsoft's stores, tested by Microsoft, passkeys available. **Migrations:** EF, with the CI drift
  check. **Concurrency:** xmin, already working for the two-mage attack. **Backups:** pg_dump, unchanged.
  **Tests:** unchanged. **Hosting:** `IGameModel(ModelBuilder)` and hooks taking `HostDb` stay EF-shaped.
  **Startup:** EF model build + `MigrateAsync` (judgement: a few hundred ms; not a problem for a long-lived server).
  **Deps:** 3 EF packages (all Microsoft or Npgsql-team).
- **Fact:** zero work. **Judgement:** EF is doing light work here, but each thing it does is done correctly and for
  free. The costs are the generated migration files (~1,200 lines for one migration), the EF-shaped hook surface, and
  the conceptual weight of a change tracker for jsonb blobs.

### Option B: Raw Npgsql everywhere + own Identity store + tiny migrator (no EF) ← strongest "leave EF" option

- **Changes (judgement, ~4–7 hobby days):**
  - `PgUserStore` on `UserStoreBase` + `IUserPasskeyStore` (~500 lines) + schema SQL; replace
    `AddEntityFrameworkStores<HostDb>()` with `AddUserStore<PgUserStore>()`.
  - `HostDb` becomes a small class around `NpgsqlDataSource` with `OpenTransactionAsync()` and helpers
    (`QuerySingleAsync<T>`, `ExecuteAsync`); ~150 lines. Replace ~15 save/execute sites plus the LINQ reads in GameService, AdminService and AccountSignals with SQL.
  - Migrator: embedded `Migrations/0001_initial.sql`, `0002_….sql`; table `schema_versions(id, applied_at)`; apply in
    one transaction each under `pg_advisory_xact_lock` so two hosts can't race. ~60 lines. (Or DbUp, needs approval.)
  - `HostUserManager.DeleteAsync` keeps its shape: the store must use the same connection/transaction as the game's
    deletion hook (pass an ambient `NpgsqlTransaction` via a scoped "unit of work"; this is the fiddliest part).
- **Identity:** own store, see 1.1. Risk bounded by tests; passkeys possible (5 methods + a table).
- **Migrations:** plain SQL, reviewable, no generated designer files. **Lost:** "CI fails if the model changed
  without a migration" — there is no model. Replace with: `TestDatabase.CreateTemplate` runs all scripts on an empty
  DB (already effectively the case), plus a test that maps every column the code reads (a smoke test per table).
- **Concurrency:** explicit `version int` column (`UPDATE mages SET data=@d, version=version+1 WHERE round=@r AND
  owner_id=@o AND version=@v`), check row count = 1. Attack: both mage updates + both news upserts in one
  `NpgsqlTransaction`; any 0-row update → rollback → "busy, try again" (same UX as today). Keeping xmin is also
  possible (fact: Npgsql 10.0.3 knows the `xid` type and the current migration maps `xmin` as `uint`/`xid`), but an
  explicit column is clearer and survives `pg_dump`/restore semantics without surprises (judgement).
  **Pessimistic alternative (from the game research, 6.2):** OGameX locks the planet row with `lockForUpdate()`
  (`SELECT … FOR UPDATE`) on every update. With raw SQL, the attack can `SELECT … FOR UPDATE` both mage rows in a
  fixed order (by `MageId`, to avoid deadlocks), so a simultaneous action *waits* instead of failing with "busy, try
  again". A double-tap then becomes "second request runs after the first and sees the spent turns", which the domain
  already refuses cleanly. EF can also do this via raw SQL, so it is not by itself a reason to leave EF.
- **Backups:** unchanged. **Tests:** template DB migrated by the script runner instead of `Database.Migrate()`;
  `OpenDb()` returns the new data class. **Hosting boundary:** `IGameModel.Configure(ModelBuilder)` becomes
  `IGameModel.Migrations` (embedded SQL scripts, prefixed e.g. `game_0001_…`) and hooks take
  `(NpgsqlConnection, NpgsqlTransaction)` or the host's unit-of-work type; `HostingBoundaryTests` keeps working.
  **Startup:** faster (no model build), negligible either way. **Deps:** −3 packages, +`Npgsql` (approved). Net: only
  Npgsql + shared framework.
- **Judgement:** the cleanest end state for this codebase; also the most hand-written code and the most security-
  relevant code you then own.

### Option C: Dapper (+ DbUp) everywhere + own Identity store

- Same as B, but Dapper removes the parameter/reader boilerplate (~30–40% less data code), and DbUp replaces the
  hand-rolled migrator. **Deps:** +Dapper, +dbup-core/dbup-postgresql → **owner approval needed** for both.
  Dapper.AOT exists for trimming/AOT but is not needed here.
- **Judgement:** for ~7 tables of key + jsonb, Dapper's win is small. A 20-line `Query<T>` helper over Npgsql gets
  most of it. Choose C over B only if the owner prefers a well-known library to a home-grown helper.

### Option D: Marten for game data (+ EF kept for Identity) or Marten for everything

- **Fit (fact + to verify):** Marten is a document store + event store on Postgres, MIT, active, net10. Mage as a
  document is natural. **To verify before choosing:** (1) the `(round, owner)` composite key: Marten documents have
  one `Id`; idiomatic options are id = `"{round}/{owner}"` or **conjoined tenancy with tenant = round** (which also
  makes the round reset elegant); (2) plugging `DomainJson`'s field-based System.Text.Json options into Marten's
  serializer (Marten supports STJ with custom options, but private-field round-trip needs testing); (3) enlisting a
  Marten session in an existing `NpgsqlTransaction` for account deletion if EF keeps Identity (Marten can open a
  session on an existing connection/transaction, from my knowledge; verify).
- **Identity:** either keep EF (then EF stays, deps go *up*), or a community Marten store (see 2.2: small projects,
  Marten.AspNetIdentity licence not stated on NuGet; Innovorium has passkeys but ~200 downloads), or own store.
- **Migrations:** Marten auto-creates/patches its tables ("Weasel"); no migration files for documents. Upcasting and
  event versioning are first-class if the actions log later becomes event-sourced.
- **Concurrency:** Marten optimistic concurrency per document (`UseOptimisticConcurrency`) and multi-document
  `SaveChangesAsync` in one transaction → the two-mage attack works.
- **Backups:** pg_dump, unchanged. **Tests:** real Postgres; Marten creates schema on first use (or apply up-front).
  **Hosting:** `IGameModel` becomes "register documents with `StoreOptions`". **Startup:** schema check + code gen at
  startup (Marten pre-generates or generates at runtime; judgement: seconds in dev unless pre-built).
  **Deps:** Marten + JasperFx + JasperFx.Events + Weasel.Postgresql + Weasel.Storage + RecyclableMemoryStream (≥ 6
  non-Microsoft packages), plus EF if Identity stays on EF.
- **Judgement:** Marten is the best tool *if* the game moves to event sourcing. With the owner's decision (snapshots
  are the truth, an append-only actions table later, upcasters for jsonb), Marten mostly re-implements what
  `DomainJson` + a jsonb column already do, at the highest dependency cost and some vendor-direction risk.

### Option E: linq2db (+ FluentMigrator or scripts)

- Typed LINQ without change tracking; nopCommerce's choice. `linq2db.Identity` exists (netstandard2.0, Dec 2025)
  but passkey support is unlikely. **Deps:** linq2db (+ analyzers), maybe FluentMigrator → approval.
- **Judgement:** worthwhile for apps with many relational queries. Spellbound's queries are "get row by key, update
  blob", so linq2db's strength is unused. Not recommended.

### Option F: SQLite (Microsoft.Data.Sqlite), e.g. one file per round

- **Fact:** `Microsoft.Data.Sqlite` is Microsoft-provided, so it's the one other engine with no approval needed. The
  project started on EF/SQLite (commit history) before RavenDB.
- **Idea:** `round-0042.db` per round; a reset = close file, start a new one; archive by copying a file.
- **Judgement:** not worth it now. Postgres has to stay for Identity/host data (or Identity moves to SQLite too), so
  per-round files mean two engines or cross-file transactions (account deletion must delete game data atomically,
  which `ATTACH` can do in SQLite but adds friction). Backups become two mechanisms. Revisit only if Postgres is dropped
  entirely for a single-file deployment.

### Option G (creative, downgraded after section 6): in-memory round state, single writer per mage, snapshots + actions log in Postgres

- **Idea:** load the current round's mages into memory at startup (a few thousand mages × a few KB = a few MB).
  Each action runs under a per-mage lock (attacks take both locks in a fixed order, e.g. by `MageId`), mutates the
  in-memory mage, then writes **in one Postgres transaction**: the jsonb snapshot(s), the news row(s), and one row in
  the append-only `actions` table the owner already plans. If the commit fails, reload that mage from Postgres
  (the snapshot is still the truth). Rankings become an in-memory sort with no DB read.
- **Why it fits (judgement):** it is *compatible* with the owner's decision (snapshots are the source of truth; actions
  log alongside; upcasters for jsonb) and keeps pg_dump backups. Double-tap and two-mage atomicity become a
  process-local lock-ordering problem plus the DB transaction; the DB version check stays as a belt-and-braces guard.
  Blazor Server already pins all players to one process, so "one process" costs nothing new. It pairs naturally
  with live updates: when a mage is attacked, notify the target's circuit directly (in-process event), no polling.
- **Costs:** it bends the "thin web layer, load-call-save" rule and introduces in-process state that must be
  invalidated on admin edits, bans and round reset; tests need to cover reload-after-failed-commit; a second app
  instance would need Postgres `LISTEN/NOTIFY` or must be ruled out. It doesn't by itself remove EF; it's orthogonal
  to the A/B choice and can be added later.
- **Revised after the game research (section 6):** Lichess, the best-known in-memory-first game server, writes
  through immediately for rated *correspondence* games and only batches flushes (every 30 s) for fast games
  (sourced from its code, see 6.2). Spellbound is entirely "correspondence-speed". **Downgraded: not needed at this
  scale;** keep it as a future optimisation if per-action database latency ever shows up in measurements.
- **Prevalence proper** (Prevayler/Memstate: no DB, only a command journal + snapshots on disk) would remove Postgres
  for game data, but Memstate is dormant since 2023 and Identity would still need a DB. Not recommended.
- **Orleans grains** (one grain per mage, ADO.NET/Postgres grain storage): technically elegant, but heavy (many
  Microsoft packages, a silo, its own storage schema) for one server. Not worth it at this scale (judgement).

### Smaller creative ideas worth keeping in the back pocket

- **LISTEN/NOTIFY for live Blazor updates** (from my knowledge, not verified here: Npgsql exposes
  `NpgsqlConnection.Notification` + `WaitAsync`; it needs one dedicated, long-lived, non-multiplexed connection): an
  `AFTER INSERT` trigger on `news` (or `pg_notify` in the attack transaction) lets the defender's open page show
  "You were attacked" instantly. Works with A or B; trivial within one process (an in-memory event works too).
- **Postgres functions for the hot path:** e.g. `take_unseen_alerts(round, owner)` already exists as one UPDATE; more
  logic in SQL would split rules out of `Spellbound.Domain`, which violates the architecture rules. **Not recommended**
  beyond such counters.
- **Round reset as `TRUNCATE`/partition drop:** partition `mages` and `news` by `round` (`PARTITION BY LIST (round)`);
  archiving or deleting an old round becomes `DETACH`/`DROP PARTITION`. Cheap with raw SQL migrations, awkward in EF.
- **Whole round in one jsonb document:** simple, but every action rewrites a multi-MB row and serializes all
  players' writes on one row lock. **Not recommended.**
- **COPY** (Npgsql binary import/export) for fast hall-of-fame snapshots or round archives: unnecessary at this scale.

---

## 4. Cross-cutting notes

### 4.1 Comparison table

| Option | New deps (approval?) | Identity | Migrations | Concurrency (double-tap / 2-mage) | Effort to switch | Main risks |
|---|---|---|---|---|---|---|
| **A. Keep EF** | none | Microsoft EF stores (+passkeys available) | EF + CI drift check | xmin, works | 0 | Generated-file noise; EF-shaped hooks |
| **B. Raw Npgsql + own store + SQL migrator** | none (−3 EF pkgs) | own `UserStoreBase` subclass, ~500 lines | `.sql` files + `schema_versions` (~60 lines) | `version` column + tx | ~4–7 days | Owning security-relevant persistence; no drift check |
| **C. Dapper + DbUp + own store** | Dapper, DbUp (yes) | as B | DbUp scripts | as B | ~3–6 days | as B, + 2 deps |
| **D. Marten (+EF or community store for Identity)** | ≥6 JasperFx pkgs (yes) | EF kept, or small community store | auto schema; event upcasting | Marten optimistic concurrency | ~4–8 days | Highest deps; vendor direction; serializer fit |
| **E. linq2db (+FluentMigrator)** | 1–2 (yes) | linq2db.Identity (no passkeys likely) or own | FluentMigrator | version column | ~4–6 days | Strength unused here |
| **G. In-memory round + snapshots/actions (on A or B)** | none | unchanged | unchanged | per-mage locks + DB tx | ~2–4 days on top | In-process state invalidation; rule tension. Downgraded after section 6: not needed at this scale |

### 4.2 Startup time (judgement)
All options are dominated by Postgres connection and (for EF) model build + migrate check; none matters for a
long-running server. The RavenDB days had the slow startup (embedded server); that problem is gone in every option.

### 4.3 What leaving EF costs that is easy to forget
- The "model changed without a migration" CI check. With hand-written SQL, drift is caught only by tests that run
  queries against the migrated schema (the integration tests already do this for every code path they cover).
- `ExecuteUpdate`/`ExecuteDelete` and LINQ convenience for admin queries.
- Microsoft-tested Identity persistence and the ready-made passkey store/schema.
- Docs and searchability: every Identity tutorial assumes EF.

### 4.4 Hosting boundary under each option
- A: unchanged. B/C: hooks take a host-owned unit of work (`HostSession` wrapping `NpgsqlConnection` +
  `NpgsqlTransaction`), `IGameModel` contributes SQL scripts. D: hooks take a Marten `IDocumentSession`.
  In all cases `HostingBoundaryTests` can stay as is.

---

## 5. Recommendation

**Fact base:** EF does three real jobs here; two (migrations, concurrency) are cheap to replace; the third (Identity)
is ~500 lines on a Microsoft base class that's already in the shared framework. No community non-EF Identity store is
mature enough to prefer over writing one.

**Judgement:**

1. **Keep EF for now (Option A).** It is worth keeping *for this project at this moment* for one reason: it gives
   Microsoft-maintained Identity persistence (including passkeys, which the owner removed in the RavenDB days and can
   now restore) with zero code, and the project has just finished a database migration. The remaining costs
   (generated migration files, EF-shaped hooks) are cosmetic. Concretely: don't add more EF *features* to game code;
   keep game access as "load row by key, deserialize, save with version", so that a later switch stays mechanical.
2. **If the owner wants EF gone, do Option B, not C/D/E**, and do it as one focused change:
   - Identity stores → `PgUserStore : UserStoreBase<ApplicationUser,string,…>, IUserPasskeyStore<ApplicationUser>`
     on Npgsql, with a dedicated store test class (lockout, stamp, 2FA + one-time recovery code, external login,
     passkey add/find/remove, concurrency-stamp conflict).
   - Migrations → embedded `.sql` scripts + `schema_versions` + advisory lock, run in `UseGameHostAsync`; game scripts
     contributed through `IGameModel`.
   - xmin → explicit `version int` columns checked in `UPDATE … WHERE version = @v`; two-mage attack in one
     `NpgsqlTransaction`.
   - `HostDb` → a ~150-line data-source/unit-of-work class; hooks take it instead of an EF context.
   - Result: zero non-Microsoft dependencies besides Npgsql (already approved).
3. **Skip Marten** unless the game later adopts real event sourcing; with snapshots as the truth it duplicates
   `DomainJson` + jsonb at the highest dependency cost.
4. **Option G (in-memory round state) is downgraded** after the game research (section 6): even Lichess writes
   slow, turn-based games straight through to the database. Keep the per-action read-modify-write against Postgres.
   Live "you were attacked" updates need only an in-process event (or LISTEN/NOTIFY), not in-memory state.
5. **From comparable games (section 6):** keep the lazy per-player turn accrual (OGameX does the same and it needs no
   scheduler); keep all rounds in one database keyed by round (as OpenDominion does) rather than a database or
   schema per round; when the actions log is built, store each action's named event and its result/delta
   (OpenDominion's `HistoryService` can rebuild a dominion at any point in time this way). Consider `SELECT … FOR
   UPDATE` in fixed `MageId` order for attacks (OGameX-style pessimistic locking) so simultaneous actions wait instead
   of failing with "busy, try again"; it works under both A and B.

**Did the game-world evidence change A vs B? No.** None of the comparable games uses ASP.NET Identity (they hand-roll
accounts in a framework's default ORM: Laravel/Eloquent, Lichess's own user module, a bare users table in Archmage
Reimagined), and no open-source C# strategy game of any maturity turned up. So the evidence says nothing about the one
thing that decides A vs B (the Identity store). What it does say is that the genre converges on "one SQL database, a
row (or blob) per player, transactions, lazy or bulk ticks", which both A and B already do.

**Direct answer — is EF worth keeping?** Narrowly yes, for now, because of Identity. If not, here is exactly what
replaces each thing it does:

| EF does | Replacement |
|---|---|
| Identity EF stores | own `UserStoreBase` subclass + `IUserPasskeyStore` on Npgsql (~500 lines, shared framework, no package) |
| Migrations + drift check | embedded `.sql` scripts + `schema_versions` + advisory lock (~60 lines); drift caught by integration tests |
| xmin concurrency | `version int` column, `UPDATE … WHERE version = @v`, row-count check; multi-row in one `NpgsqlTransaction` |
| DbContext/LINQ/ExecuteUpdate | ~150-line `HostDb` over `NpgsqlDataSource` with a few typed helpers |
| `IGameModel(ModelBuilder)` hook | game contributes SQL scripts; hooks take the host's unit-of-work |

---

## 6. Comparable games (follow-up research)

The first comparison used business apps (Bitwarden, nopCommerce, Orchard, Jellyfin). This section looks at
web-based multiplayer strategy games, mostly turn- or tick-based persistent browser games (PBBGs).

**Method and limits.** Where a game is open source I read its code directly (raw files from GitHub). Commercial games
(OGame, Travian, Tribal Wars, Torn, Kingdom of Loathing, Politics & War, Earth Empires, Planetarion, Utopia) publish
almost nothing about their persistence. Searches found only engine/language facts from Wikipedia and similar, and no
engineering blogs, talks or postmortems about their databases. The PBBG community (r/PBBG, pbbg.com, the Discord)
could not be reached from this sandbox (Reddit blocked; searches returned nothing usable). So the strongest evidence
is open-source code, labelled **[code]**. **[secondary]** = Wikipedia, press or a third-party page.
**[inferred]** = my reading.

### 6.1 Games and projects found

| Game / project | Stack | Persistence | Patterns worth noting | Source |
|---|---|---|---|---|
| **OpenDominion** (open-source Dominion clone; same genre as Archmage: land, buildings, spells, ops, rounds); 264 stars, ~7,700 commits | PHP 8.5, Laravel 13 | MariaDB/MySQL via Eloquent ORM | One DB, `round_id` on dominions; hourly **global tick** as one bulk `UPDATE dominions JOIN dominion_tick` in a transaction; actions wrapped in `DB::transaction`; `HistoryService` records a named event per action (`explore`, `invade`, `invaded`, …) and can return "a dominion with state at a certain time"; `guardActionsDuringTick` (name only, not read) | [code] [repo](https://github.com/OpenDominion/OpenDominion), [composer.json](https://raw.githubusercontent.com/OpenDominion/OpenDominion/develop/composer.json), [.env.example](https://raw.githubusercontent.com/OpenDominion/OpenDominion/develop/.env.example), [TickService](https://raw.githubusercontent.com/OpenDominion/OpenDominion/develop/src/Services/Dominion/TickService.php), [InvadeActionService](https://raw.githubusercontent.com/OpenDominion/OpenDominion/develop/src/Services/Dominion/Actions/InvadeActionService.php), [HistoryService](https://raw.githubusercontent.com/OpenDominion/OpenDominion/develop/src/Services/Dominion/HistoryService.php) |
| **Archmage Reimagined** (modern Archmage remake); 7 stars | TypeScript, Node 20, Vue 3 | **PGlite** (Postgres compiled to WASM, embedded in-process, `new PGlite('./archmage-db')`), or an in-memory/file adapter for tests | Each mage is **one JSON blob** (`mage(username, id, mage json)`), own `archmage_user` table (username + bcrypt hash + token); **global turn tick** = `UPDATE mage SET mage = jsonb_set(mage, '{currentTurn}', …+1)`; battle reports and chronicles as JSON rows; SQL built by string interpolation, no visible concurrency control | [code] [repo](https://github.com/mwdchang/archmage-reimagined), [data-adapter package.json](https://raw.githubusercontent.com/mwdchang/archmage-reimagined/master/packages/data-adapter/package.json), [pglite-data-adapter.ts](https://raw.githubusercontent.com/mwdchang/archmage-reimagined/master/packages/data-adapter/src/pglite-data-adapter.ts) |
| **OGameX** (open-source OGame clone) | PHP, Laravel 12 | MySQL; Laravel database queue for fleet missions | **Lazy accrual**: resources computed from `time_last_update` "on every users page load and every time the planet is touched" (the same idea as Spellbound's `TurnClock`); **pessimistic locking** `lockForUpdate()` on the planet row; "atomically deduct resources and units in a single transaction" for fleet dispatch | [code] [.env.example](https://raw.githubusercontent.com/lanedirt/OGameX/main/.env.example), [PlanetService.php](https://raw.githubusercontent.com/lanedirt/OGameX/main/app/Services/PlanetService.php), [repo](https://github.com/lanedirt/OGameX) |
| **OGame** (Gameforge) | PHP + AJAX | not public (MySQL commonly assumed, unconfirmed) | many parallel universes | [secondary] [Wikipedia](https://en.wikipedia.org/wiki/OGame) |
| **Travian** / **Tribal Wars** (InnoGames) | PHP (Travian) | not public | parallel worlds with different settings; Tribal Wars started on a few rented single-CPU servers and handled "120 player attacks arriving every second" on one world | [secondary] [Travian](https://en.wikipedia.org/wiki/Travian), [Tribal Wars](https://en.wikipedia.org/wiki/Tribal_Wars), [MCV](https://www.mcvuk.com/development-news/how-browser-based-tribal-wars-has-survived-for-14-years/) |
| **Utopia** | Python (rewritten from scratch in 2009 after a DDoS) | not public | ~10–12-week "ages", world resets each age | [secondary] [Wikipedia](https://en.wikipedia.org/wiki/Utopia_(1998_video_game)) |
| **Neptune's Pride** | Google App Engine (2010) | App Engine datastore (inferred from the platform) | — | [secondary] [Google Cloud blog, Apr 2010](https://cloudplatform.googleblog.com/2010/04/) (interview headline only; page unreachable) |
| **Lichess** (non-.NET reference for live game state) | Scala 3, Play, Pekko actors | MongoDB; Redis between the websocket server and the app | One **in-memory async actor per game** (`RoundAsyncActor`, single writer); `GameProxy` keeps the game cached and flushes "dirty progress" to Mongo **after up to 30 s**, but **immediately** on a status change, simul, or **rated correspondence** game | [code] [GameProxy.scala](https://raw.githubusercontent.com/lichess-org/lila/master/modules/round/src/main/GameProxy.scala), [RoundAsyncActor.scala](https://raw.githubusercontent.com/lichess-org/lila/master/modules/round/src/main/RoundAsyncActor.scala) |
| **Screeps** (non-.NET reference, tick-based MMO) | Node.js | MongoDB (each game object a document) + Redis (tick sync and work queues) | Two-stage global tick (player scripts, then command processing); the dev team says "the database is the bottleneck", and sharding the DB made it worse | [code/docs] [architecture](https://docs.screeps.com/architecture.html), [forum](https://screeps.com/forum/post/513) |
| Other open-source PBBGs: MCCodes v2, Space Merchant Realms, Flare, Might & Fealty, MERN-template | PHP (MCCodes, SMR, Flare, M&F); Node/React (MERN-template) | MySQL/MariaDB throughout (SMR: `pdo_mysql` + doctrine/dbal; MERN-template: Sequelize + MariaDB despite its name) | GitHub's `pbbg` topic: **no C# repositories among the 20 shown**; the top items are PHP | [code] [topic](https://github.com/topics/pbbg), [SMR composer](https://raw.githubusercontent.com/smrealms/smr/main/composer.json), [MERN-template package.json](https://raw.githubusercontent.com/krgamestudios/MERN-template/main/package.json) |
| .NET examples | Xen (OGame-like, C#, EF Core + PostgreSQL, early, no playable prototype); SCom (ASP.NET + SQL Server, 2007) | — | No mature open-source .NET browser strategy game found | [secondary, unverified] [search snippet for Xen](https://zff.dev/josejfernandez/Xen) (page unreachable), [SCom](https://sourceforge.net/p/scom) |
| Torn, Kingdom of Loathing, Politics & War, Earth Empires, Planetarion, Ikariam | — | **stack not publicly documented** (only old forum hearsay: KoL "php/mysql") | — | none usable |

### 6.2 Patterns that recur

1. **One relational database, one row (or JSON blob) per player, transactions per action.** [code: OpenDominion,
   OGameX, Archmage Reimagined, SMR, MCCodes] None of the five open-source games read here uses event sourcing, a document database or an
   actor framework for the main game state. The exceptions (Lichess, Screeps) are real-time games with far higher
   write rates. [inferred] Spellbound's "jsonb snapshot per mage per round, keyed columns" is squarely the genre norm.
   Archmage Reimagined even uses the same shape: a JSON blob per mage, updated with `jsonb_set`.
2. **Two tick models.** *Global tick*: a scheduled bulk update of every player (OpenDominion hourly, Archmage
   Reimagined `nextTurn`, Screeps). *Lazy per-player accrual*: store a timestamp and catch up when the player is
   touched (OGameX `time_last_update`, Spellbound `TurnClock`). [inferred] Lazy accrual needs no scheduler and no
   "actions blocked during tick" window; global ticks need a guard like OpenDominion's `guardActionsDuringTick`.
   Spellbound's choice is the simpler one.
3. **Concurrency is usually pessimistic or absent.** OGameX: `SELECT … FOR UPDATE` on the planet row plus atomic
   deductions [code]. OpenDominion: `DB::transaction` around actions; no row lock in the two action services I read
   (I didn't read the whole tree) [code, partial]. Archmage Reimagined: none (single Node process, interpolated SQL)
   [code]. Lichess: one actor per game serializes all moves [code]. [inferred] Spellbound's optimistic xmin check is
   *more* careful than most of the genre. Row locks in a fixed order are a reasonable alternative (section 3, B).
4. **Rounds and worlds.** OpenDominion keeps every round in one database with a `round_id` column [code], exactly like
   Spellbound's `(round, owner)` key. Archmage Reimagined has one round per database and `resetData()` drops tables
   [code]. OGame, Travian and Tribal Wars run many **parallel** worlds/universes with different settings
   [secondary]. Whether each has its own database isn't public; it is commonly assumed but unverified.
5. **History and battle reports.** OpenDominion: a per-action history event from which past state can be rebuilt
   [code]. Archmage Reimagined: `battle_report` and `turn_chronicle` tables holding JSON [code]. [inferred] Both
   validate the owner's plan for an append-only actions table, and suggest storing each action's result/delta (or
   report) alongside its name, not only the command.
6. **Accounts are hand-rolled or the framework default.** OpenDominion and OGameX use Laravel's auth on the same
   database; Archmage Reimagined has its own users table with bcrypt; Lichess has its own user module [code / partly
   from my knowledge for Lichess]. None uses anything like ASP.NET Identity's 2FA/recovery-code surface, so they
   offer no evidence for or against an EF-based Identity store.
7. **In-memory-first only when speed demands it.** Lichess's `GameProxy.shouldFlushProgress` writes through at once
   for rated correspondence games and defers only for fast games [code]. Screeps names the database as its
   bottleneck at tick scale [docs]. [inferred] For a game where a player acts a few times an hour, writing through is
   what even the in-memory specialists do.

### 6.3 What this changes for Spellbound

- **A (keep EF) vs B (raw Npgsql): unchanged.** The genre's choice is "the web framework's default ORM on one SQL
  database" (Eloquent in every PHP example). For an ASP.NET Core app that default is EF Core, which slightly favours
  A. The Identity question, which decides A vs B, isn't touched by any of this evidence.
- **G (in-memory mages): downgraded** to "not needed". Lichess is the counterexample that matters: it writes through
  for slow, turn-based games.
- **One database or schema per round/world: not recommended.**
  - The OGame/Travian/Tribal Wars pattern is about *parallel* worlds that run at the same time with different
    settings, not sequential rounds. Their storage layout isn't public anyway.
  - Spellbound has one world and sequential rounds, and accounts, bans and the hall of fame span rounds. A
    database per round would split account deletion (which must remove game data in the same transaction) and the
    hall of fame across databases, multiply migrations, and complicate pg_dump.
  - The `(round, owner)` key already isolates rounds, as OpenDominion's `round_id` does. If old rounds ever need
    archiving or dropping cheaply, use `PARTITION BY LIST (round)` on `mages` and `news` (section 3, smaller ideas).
  - [judgement] A schema per round only becomes interesting if Spellbound ever runs parallel worlds (e.g. a "blitz"
    server next to the normal one). Then a `world` key column is still simpler than separate schemas.
- **Small, concrete takeaways:**
  - Keep lazy accrual.
  - Consider fixed-order `SELECT … FOR UPDATE` for attacks.
  - Design the actions log OpenDominion-style: a named event plus the result/delta per action, which makes battle
    reports, admin investigation and "state at time T" possible without full event sourcing.
