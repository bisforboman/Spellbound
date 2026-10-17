# Spellbound roadmap

## M0: Bootstrap
- [x] Design draft, architecture notes, CLAUDE.md
- [x] Domain rules: turn clock, mage economy, explore/build/meditate/cast, school ring, starter spells
- [x] Domain unit tests
- [x] Run `scripts/bootstrap.ps1`, make the build and tests pass, then make the first commit

## M1: Core solo loop
Goal: you can sign up on a phone, create a mage and play the economy loop.

- [x] Bind `GameRules` from configuration and register `TimeProvider.System`
- [x] EF Core mapping for `Mage` (owned `ActiveEffects`, concurrency token, unique `OwnerId`) and a migration
- [x] `GameService` in the web project (load → domain action → save, with concurrency handling)
- [x] "Create your mage" page: name and school, shown after registration if the user has no mage
- [x] Overview page: land/buildings/gold/mana, turns with "next turn in …" and turn cap, active spells
- [x] Action pages: Explore, Build, Meditate, Cast. Each shows its cost up front and renders the `ActionReport` afterwards
- [x] News log: persist the last 50 reports per mage and show them on the overview
- [x] Mobile layout pass at 360 px: bottom navigation, big buttons, no horizontal scroll
- [x] PWA manifest and icons so the game can be added to the home screen
- [x] Integration test for the action flow (WebApplicationFactory with in-memory SQLite)

## M2: PvP
- [ ] Units: recruit, upkeep, and an army page
- [ ] Attack another mage: land taken, losses, battle report (deterministic, seedable RNG)
- [ ] Offensive spells against a target, with school counter bonuses and barriers
- [ ] Protection for new mages
- [ ] Rankings (land and networth)
- [ ] Notifications when you're attacked (news log, optional web push)

## M3: Rounds and hosting
- [ ] Rounds: start/end dates, reset, hall of fame
- [ ] Dockerfile and docker-compose with Caddy, deployed to a VPS
- [ ] Backups, Cloudflare, basic rate limiting and anti-multi-account checks
- [ ] Admin page: server settings, ban, reset

## Later
- Research, artifacts, guilds and chat, server variants, a stored-turns bank
