# Spellbound

A text-first multiplayer strategy game for phones: a dark hex map, an opening draft, a stance draft each round,
Imps and Golems, 8 rounds, points banked as you go. It is a clean-slate design (19 Oct 2026); the old game (the Realm,
an Archmage-style strategy game, and its Mageborn rework) was removed and is in the git history at `b171e46`. The
platform it ran on stays; the new game plugs into it as `SpellboundGame`, its matches started on demand.

Read these before making changes:
- `docs/design-principles.md`: what kind of game it is, its target players, and the principles rules are weighed against
- `docs/game-design.md`: the rules and first-guess numbers (the source of truth for gameplay)
- `docs/skirmish-paper-prototype.md`: the one-page version for a paper playtest
- `docs/roadmap.md`: milestones and the current checklist
- `docs/architecture.md`: stack, project layout, the game host boundary, hosting plan
- `docs/theme.md`: the art theme

## First run

If `src/Spellbound.Web` does not exist yet, the solution has not been bootstrapped. Run:

```powershell
./scripts/bootstrap.ps1
```

It creates the `.slnx`, the Blazor Server web project (from the official template; it has since moved to Google sign-in and PostgreSQL),
the project files for the domain and test projects, and wires up references. Then build and test.

## Commands

```powershell
docker compose -f docker-compose.dev.yml up -d   # a local PostgreSQL, needed by dotnet test and dotnet run
dotnet build
dotnet test
dotnet run --project src/Spellbound.Web
dotnet run --project tools/Spellbound.Playtest     # plays every page in a phone-sized browser; see .claude/skills/playtest
dotnet run --project tools/Spellbound.Simulator -- --players 3 --games 1000   # bot-vs-bot balance report (Markdown)
dotnet run --project tools/Spellbound.Simulator -- --players 3 --games 2000 --plans   # strategy bots: does each plan pay off?
dotnet run --project tools/Spellbound.Simulator -- --plans --set AttunePricePerTile=3   # measure a rule change before making it
```

To change a table (not the jsonb game state), add a numbered SQL script: `src/Spellbound.Web/Hosting/Sql/` for the
host's tables, `src/Spellbound.Web/Data/Sql/` for the game's. It runs at the next start. Never edit a released script.

## Architecture rules

The first group held for the old game and still fits the new design (rounds played in turn, resolved from a
seed); revisit it as M1 is built, and don't assume the old game's turns or mages exist.

- **All game rules live in `Spellbound.Domain`.** It is pure C#. It has no database, ASP.NET or I/O references,
  and it never reads the clock. Callers pass `DateTimeOffset now` in.
- **Balance numbers live in one rules type**, stored with each game, never hardcoded elsewhere (the old game's was
  `GameRules`, bound from configuration as the preset for the next round).
- **Actions either fully succeed or change nothing**, and say what happened in short, readable lines.
- **No per-player timers or background loops.** Background jobs are only for global events such as ending games
  at their end (`GameKeeper`): one job for every game.
- **The web layer is thin:** load, call one domain method with `TimeProvider.GetUtcNow()`, then save, in one
  transaction, with the player's row locked (`SELECT … FOR UPDATE`) so a double tap waits for the first.
- **Make the domain deterministic.** Any randomness comes through an injected, seedable source, so it can be replayed
  in tests.

These hold for the platform, whatever the game:

- **`Hosting/` is the reusable game host and never depends on the game.** It may use `Round`, nothing else from
  `Spellbound.Domain` or the game's web code; the game plugs in through `Hosting/GameHooks.cs`. `HostingBoundaryTests`
  fails if this is broken. See `docs/architecture.md`, "The game host boundary".

## UI rules

- Mobile-first. The UI must work at 360 px wide, with tap targets at least 44 px, and work one-handed.
- Pages are text-first: numbers, short reports and simple forms. No canvas or game engine, and no JS framework.
- Every action form shows its cost and its projected result before the player confirms (the old game's "ledger").
- A change to pages gets a playtest run (`tools/Spellbound.Playtest`): it must pass, and look at the screenshots of the
  pages you changed. New pages and actions get a step in it.

## Working conventions

- C# latest, nullable enabled, file-scoped namespaces. Use records for value types and keep classes small.
- Each new rule gets a unit test in `tests/Spellbound.Domain.Tests` that covers the happy path and the "changes nothing on failure" case.
- When a rule or number changes, update the design document in the same change.
- Tick off items in `docs/roadmap.md` as they are completed.
- Keep dependencies minimal. Ask before adding a NuGet package that isn't Microsoft-provided.
- **Advisor**: when an advisor tool is available, call it before design decisions or recommendations, before splitting
  work across agents or sessions, and before declaring a larger task done.
- **Where work runs**: long work (multi-step tasks, PRs, CI waits, releases) runs as cloud sessions; the owner's
  computer must stay free to restart or turn off. Work that needs it (local timings, Windows-only checks) is asked for,
  never started unasked; a cloud session that needs such a step says so in its PR.
- **Asking the owner**: when you need input, a decision or an action only the owner can take (a key, a setting, an
  approval), ask with the question prompt (the AskUserQuestion tool) so it can't be missed, instead of leaving the
  question in a summary. Ask once, with the options and your recommendation, and keep working on what doesn't depend
  on the answer.
