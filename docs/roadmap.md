# Spellbound roadmap

## M0: Bootstrap
- [x] Design draft, architecture notes, CLAUDE.md
- [x] Domain rules: turn clock, mage economy, explore/build/meditate/cast, school ring, starter spells
- [x] Domain unit tests
- [x] Run `scripts/bootstrap.ps1`, make the build and tests pass, then make the first commit

## M1: Core solo loop
Goal: you can sign up on a phone, create a mage and play the economy loop.

- [x] Bind `GameRules` from configuration and register `TimeProvider.System`
- [x] Persistence for `Mage` (RavenDB documents keyed by owner, optimistic concurrency)
- [x] `GameService` in the web project (load → domain action → save, with concurrency handling)
- [x] "Create your mage" page: name and school, shown after registration if the user has no mage
- [x] Overview page: land/buildings/gold/mana, turns with "next turn in …" and turn cap, active spells
- [x] Action pages: Explore, Build, Meditate, Cast. Each shows its cost up front and renders the `ActionReport` afterwards
- [x] News log: persist the last 50 reports per mage and show them on the overview
- [x] Mobile layout pass at 360 px: bottom navigation, big buttons, no horizontal scroll
- [x] PWA manifest and icons so the game can be added to the home screen
- [x] Integration test for the action flow (WebApplicationFactory with in-memory RavenDB)

## M2: PvP
- [x] Units: recruit, upkeep, and an army page
- [x] Attack another mage: land taken, losses, battle report (deterministic, seedable RNG)
- [x] Offensive spells against a target, with school counter bonuses and barriers
- [x] Protection for new mages
- [x] Rankings (land and networth)
- [x] Notifications when you're attacked (news log and overview banner; web push moved to Later)

## M3: Rounds and hosting
- [x] Rounds: start/end dates, reset, hall of fame
- [x] Dockerfile and docker-compose with Caddy ([deploy guide](deploy.md))
- [ ] Deployed to a VPS (needs the RavenDB Community key, a server and a domain)
- [x] Nightly backups with retention and a tested restore ([deploy guide](deploy.md#backups))
- [x] Basic rate limiting, login lockout and multi-account signals on the admin page
- [ ] Cloudflare in front (needs the domain)

### Waiting on the owner
- [ ] RavenDB Community licence key: applied for on 17 Oct 2026. When it arrives, follow
  [issue #9](https://github.com/bisforboman/Spellbound/issues/9) (check it's Community, put it in `.env`, note the expiry)
- [ ] A VPS and a domain pointing at it (see [deploy guide](deploy.md))
- [x] Admin page: server settings, ban, reset

## Later
- Research, artifacts, guilds and chat, server variants, a stored-turns bank, web push notifications
