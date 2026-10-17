# Spellbound

A turn-based, text-first multiplayer strategy game for phones, inspired by the old browser game Archmage.
Players run a mage, spend a slowly regenerating pool of **turns** to explore land, build, gather mana and
cast spells, and (from milestone 2) attack each other.

Read these before making changes:
- `docs/game-design.md`: the rules and starting numbers (the source of truth for gameplay)
- `docs/architecture.md`: stack, project layout, request flow, hosting plan
- `docs/roadmap.md`: milestones and the current checklist

## First run

If `src/Spellbound.Web` does not exist yet, the solution has not been bootstrapped. Run:

```powershell
./scripts/bootstrap.ps1
```

It creates the `.slnx`, the Blazor Server web project (from the official template, with Individual auth; it has since been moved from EF Core to RavenDB),
the project files for the domain and test projects, and wires up references. Then build and test.

## Commands

```powershell
dotnet build
dotnet test
dotnet run --project src/Spellbound.Web   # first start is slower: it launches the embedded RavenDB server
```

## Architecture rules

- **All game rules live in `Spellbound.Domain`.** It is pure C#. It has no database, ASP.NET or I/O references,
  and it never reads the clock. Callers pass `DateTimeOffset now` in.
- **Balance numbers live only in `GameRules`.** Don't hardcode costs, rates or caps anywhere else. The web app binds
  `GameRules` from configuration (section `"GameRules"`) so a server can be tuned without code changes.
- **Every domain action accrues turns first** (`Mage.AccrueTurns`) and returns an `ActionReport` of short,
  readable text lines. Actions either fully succeed or change nothing.
- **Turns regenerate lazily.** The app stores `Turns` plus `TurnsAccruedAt` and computes the rest on read.
  There are no per-player timers or background loops. Background jobs are only for global events like round resets.
- **The web layer is thin:** load the mage, call one domain method with `TimeProvider.GetUtcNow()`, then save. Use RavenDB's
  optimistic concurrency (`OptimisticConcurrencyMode.Writes`) so a double-tap can't spend the same turns twice.
- **Make the domain deterministic.** Any randomness (from milestone 2 combat) comes through an injected, seedable source so
  battles can be replayed in tests.

## UI rules

- Mobile-first. The UI must work at 360 px wide, with tap targets at least 44 px, and work one-handed.
- Pages are text-first: numbers, short reports and simple forms. No canvas or game engine, and no JS framework.
- Every action form shows its cost (turns, gold, mana) before the player confirms.
- Show the time until the next turn and how close the player is to the turn cap.

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
