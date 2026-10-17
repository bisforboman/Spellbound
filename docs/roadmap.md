# Spellbound roadmap

## M0: Bootstrap
- [x] Design draft, architecture notes, CLAUDE.md
- [x] Domain rules: turn clock, mage economy, explore/build/meditate/cast, school ring, starter spells
- [x] Domain unit tests
- [x] Run `scripts/bootstrap.ps1`, make the build and tests pass, then make the first commit

## M1: Core solo loop
Goal: you can sign up on a phone, create a mage and play the economy loop.

- [x] Bind `GameRules` from configuration and register `TimeProvider.System`
- [x] Persistence for `Mage` (rows keyed by round and owner, optimistic concurrency; RavenDB at first, PostgreSQL since M3)
- [x] `GameService` in the web project (load → domain action → save, with concurrency handling)
- [x] "Create your mage" page: name and school, shown after registration if the user has no mage
- [x] Overview page: land/buildings/gold/mana, turns with "next turn in …" and turn cap, active spells
- [x] Action pages: Explore, Build, Meditate, Cast. Each shows its cost up front and renders the `ActionReport` afterwards
- [x] News log: persist the last 50 reports per mage and show them on the overview
- [x] Mobile layout pass at 360 px: bottom navigation, big buttons, no horizontal scroll
- [x] PWA manifest and icons so the game can be added to the home screen
- [x] Integration test for the action flow (WebApplicationFactory with a real database)

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
- [x] Moved from embedded RavenDB to PostgreSQL: no licence key needed, a third of the image size, faster start and tests
- [ ] Deployed to a VPS (needs a server and a domain)
- [x] Nightly backups with retention and a tested restore ([deploy guide](deploy.md#backups))
- [x] Basic rate limiting, login lockout and multi-account signals on the admin page
- [ ] Cloudflare in front (needs the domain)
- [x] Admin page: server settings, ban, reset
- [x] Password reset by an admin (the server sends no email yet; "Forgot password" points players to the admin)
- [x] Balance pass from a round simulation ([balance](balance.md)): diminishing land on repeated attacks, 72 hours of
  protection, turn cap 240

### Waiting on the owner
- [ ] A VPS and a domain pointing at it (see [deploy guide](deploy.md))

## Later
- Research, artifacts, guilds and chat, server variants, web push notifications
- Real email (an SMTP service) so players can reset their own password and change their email address; until then
  both go through an admin
- Move `Hosting/` into its own reusable project when a second game is planned (see architecture, "The game host
  boundary")
