# Spellbound

A turn-based, text-first multiplayer strategy game for phones, inspired by the old browser game Archmage.
Players run a mage, spend a slowly regenerating pool of **turns** to explore land, build, gather mana and
cast spells, and (from milestone 2) attack each other.

Read these before making changes:
- `docs/game-design.md`: the rules and starting numbers (the source of truth for gameplay)
- `docs/skirmish-design.md`: the rules for the Skirmish mode (in the domain; no pages yet)
- `docs/architecture.md`: stack, project layout, request flow, hosting plan
- `docs/roadmap.md`: milestones and the current checklist

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
```

To change a table (not the jsonb game state), add a numbered SQL script: `src/Spellbound.Web/Hosting/Sql/` for the
host's tables, `src/Spellbound.Web/Data/Sql/` for the game's. It runs at the next start. Never edit a released script.

## Architecture rules

- **All game rules live in `Spellbound.Domain`.** It is pure C#. It has no database, ASP.NET or I/O references,
  and it never reads the clock. Callers pass `DateTimeOffset now` in.
- **Balance numbers live only in `GameRules` (the Realm) and `SkirmishRules` (Skirmish).** Don't hardcode costs, rates
  or caps anywhere else. Each game stores the rules it runs under; the web app binds `GameRules` from configuration (section `"GameRules"`) as the preset for new
  games (the next Realm round), so a server can be tuned without code changes.
- **Every domain action accrues turns first** (`Mage.AccrueTurns`) and returns an `ActionReport` of short,
  readable text lines. Actions either fully succeed or change nothing.
- **Turns regenerate lazily.** The app stores `Turns` plus `TurnsAccruedAt` and computes the rest on read.
  There are no per-player timers or background loops. Background jobs are only for global events like round resets.
- **The web layer is thin:** load the mage, call one domain method with `TimeProvider.GetUtcNow()`, then save, in one
  transaction. The mage's row is locked while it acts (`SELECT … FOR UPDATE`), so a double-tap waits for the first tap
  and then sees the turns it left: the same turns can't be spent twice.
- **Make the domain deterministic.** Any randomness (from milestone 2 combat) comes through an injected, seedable source so
  battles can be replayed in tests.

- **`Hosting/` is the reusable game host and never depends on the game.** It may use `Round`, nothing else from
  `Spellbound.Domain` or the game's web code; the game plugs in through `Hosting/GameHooks.cs`. `HostingBoundaryTests`
  fails if this is broken. See `docs/architecture.md`, "The game host boundary".

## UI rules

- Mobile-first. The UI must work at 360 px wide, with tap targets at least 44 px, and work one-handed.
- Pages are text-first: numbers, short reports and simple forms. No canvas or game engine, and no JS framework.
- Every action form shows its cost (turns, gold, mana) before the player confirms.
- Show the time until the next turn and how close the player is to the turn cap.
- A change to pages gets a playtest run (`tools/Spellbound.Playtest`): it must pass, and look at the screenshots of the
  pages you changed. New pages and actions get a step in it.

## Working conventions

- C# latest, nullable enabled, file-scoped namespaces. Use records for value types and keep classes small.
- Each new rule gets a unit test in `tests/Spellbound.Domain.Tests` that covers the happy path and the "changes nothing on failure" case.
- When a rule or number changes, update `docs/game-design.md` in the same change.
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
