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
- [x] Nightly backups with retention and a tested restore ([deploy guide](deploy.md#backups))
- [x] Basic rate limiting, login lockout and multi-account signals on the admin page
- [x] Admin page: server settings, ban, reset
- [x] Password reset by an admin (since replaced by Google sign-in, which has no passwords)
- [x] Balance pass from a round simulation ([balance](balance.md)): diminishing land on repeated attacks, 72 hours of
  protection, turn cap 240

## M4: First live round
Goal: real players finish a round, and we learn from it.

**On hold since 18 Oct 2026:** the game gets two modes, Realm and Skirmish, and launches with both (see M5, M6 and
[game design](game-design.md#modes-decided-18-oct-2026-not-built-yet)). The open items below wait until then.

- [x] Sign in with Google instead of passwords (no password storage, resets or lockouts; a Development-only sign-in for
  local runs and tests). Needs the owner's Google OAuth client before deploy
- [x] Persistence on plain Npgsql instead of EF Core: SQL migration files, row locks for concurrent actions
- [x] Privacy notice before sign-up: what's stored (email, hashed network, game data and the action log), why, how
  long, and how to delete your account
- [ ] Terms and rules page, and an "ask an admin" contact path (the server sends no email)
- [x] Alerts on unhandled errors and failed nightly backups
- [x] Action log: an append-only `actions` table written with each save (round, player, time, a named action, its
  inputs, random draws, result and a hash of the rules), like OpenDominion's history. For disputes, audits and real
  balance data
- [ ] First-round checklist: what to watch (attacks per target, whether once-a-day players stay, the hall of fame), so
  real data settles the balance questions below
- [ ] Deployed to a VPS (needs a server and a domain)
- [ ] Cloudflare in front (needs the domain)

### Waiting on the owner
- [ ] A VPS and a domain pointing at it (see [deploy guide](deploy.md))
- [ ] A Google OAuth client for sign-in (see [deploy guide](deploy.md))
- [ ] Read the privacy notice (`/privacy`) and fill in its `PRIVACY_*` settings: operator, contact, hosting, off-site
  backups (see [deploy guide](deploy.md#privacy-notice))

## M5: Games and lobbies
Goal: players create and join games; each game has its own mode and rules. Built with the Realm only first, so every
existing test still checks "one game in the list".

- [ ] A `games` table (mode, status, rules, start and end) and members, replacing the single `current_round`
- [ ] Each game stores its rules (`GameRules` plus switches); configuration becomes the default preset
- [ ] Mages, news, actions and hall of fame keyed by game instead of round; the round keeper ends every game on time
- [ ] Create a game (mode, players, switches within limits) and join one; the game page lists its rules
- [ ] The Realm as one always-on game the server keeps running

## M6: Skirmish
- [ ] Rules draft ([skirmish design](skirmish-design.md)), agreed with the owner
- [ ] Rules in the domain, with tests: orders, and playing a round from every player's orders and a seeded random source
- [ ] A simulation in `tools/` until it plays well
- [ ] Rounds with a ready-up and a time limit; the server plays a round when all are ready or time is up
- [ ] Reminders by web push (when everyone else is ready, and when time is running out)
- [ ] The board, orders and scoring pages, at 360 px, text-first
- [ ] Dropouts (away after 2 missed rounds) and a win record per account
- [ ] One luck feature to start (for example "exploring finds things"), as a switch

## Engineering
- Persistence: plain Npgsql with Google sign-in, decided 17 Oct 2026 after comparing alternatives and similar games
  ([research](research/persistence.md))
- jsonb upcasters: game state is versioned JSON; add the first upcaster at the first rename or reshape
- The `Hosting/` hooks are shaped for one round at a time; M5 makes them per game (round length, round end)
- Tests: most of their time is the app starting once per test; share one app per test class if speed matters

## Balance
See [balance.md](balance.md) and the open questions in [game design](game-design.md).
- A limit on tiny attacks, then upkeep: decide after the first live round, with real data
- Population or peasants, and an end-of-round event: design questions, not numbers

## Later
- Research, artifacts, guilds and chat, server variants, web push for Realm attacks (Skirmish reminders bring it in M6)
- Real email (an SMTP service), if the game ever needs it
- More sign-in options (Apple, passkeys), linked to the same account
- Move `Hosting/` into its own reusable project if a separate game is ever planned (see architecture, "The game host
  boundary"); Realm and Skirmish are modes of one game, so they don't need it
