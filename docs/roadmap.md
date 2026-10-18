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

**On hold since 18 Oct 2026:** the Realm is being reworked as Mageborn first (see M7). The open items below wait
until then.

- [x] Sign in with Google instead of passwords (no password storage, resets or lockouts; a Development-only sign-in for
  local runs and tests). Needs the owner's Google OAuth client before deploy
- [x] Persistence on plain Npgsql instead of EF Core: SQL migration files, row locks for concurrent actions
- [x] Privacy notice before sign-up: what's stored (email, hashed network, game data and the action log), why, how
  long, and how to delete your account
- [ ] Terms and rules page, and an "ask an admin" contact path (the server sends no email); independent of the
  rework, doable any time
- [x] Alerts on unhandled errors and failed nightly backups
- [x] Action log: an append-only `actions` table written with each save (round, player, time, a named action, its
  inputs, random draws, result and a hash of the rules), like OpenDominion's history. For disputes, audits and real
  balance data
- [ ] First-round checklist: replaced by what the Mageborn alpha measures (M7, A1)
- [ ] Deployed to a VPS (needs a server and a domain)
- [ ] Cloudflare in front (needs the domain)

### Waiting on the owner
- [ ] A VPS and a domain pointing at it (see [deploy guide](deploy.md))
- [ ] A Google OAuth client for sign-in (see [deploy guide](deploy.md))
- [ ] Read the privacy notice (`/privacy`) and fill in its `PRIVACY_*` settings: operator, contact, hosting, off-site
  backups (see [deploy guide](deploy.md#privacy-notice))

## Order from here
Decided 18 Oct 2026: the Realm is reworked as Mageborn ([design](mageborn-design.md), [rework plan](realm-rework.md))
before anything is deployed; Skirmish was removed. M7 is the backlog: its core leads to an **alpha with friends**
(A1), which is where deploying (M4) comes back; the rest of the design follows the alpha.

## M5: Games and lobbies
Goal: a server runs many games, each with its own mode and rules. In two parts, so the risky data change ships alone.

**Part 1: the data model, with the Realm only** (every existing test still passes, as "one game in the list")
- [x] A `games` table (mode, round number, start, end, rules) replacing the single `current_round`; each Realm round is
  one game, and the server starts the next one when a round ends
- [x] Each game stores its rules (`GameRules`); configuration is the preset for the next
  round
- [x] Mages, news, actions and hall of fame keyed by game instead of round
- [x] A Games page after sign-in (`/`), listing the player's games (for now, the current Realm round); the Realm's
  overview moved to `/realm`

**Part 2: creating and joining** was built for Skirmish (lobbies, invite codes, a public list) and removed with it on
18 Oct 2026; it's in the git history if a second mode comes back.
- [ ] Page addresses that name a game (`/games/{id}/…` or similar), if a second mode comes back

## M6: Skirmish (removed 18 Oct 2026)
A short board-game mode with simultaneous rounds, built to a playable state (rules, simulator, lobbies with invite
codes, web push reminders, a win record) and then removed when the owner chose to rework the Realm instead. Web push
notifications stay; the rest is in the git history.

## M7: Mageborn
The Realm reworked to the owner's research ([design](mageborn-design.md); what each part changes:
[realm-rework.md](realm-rework.md)). This list is the backlog and its order.

**Each item is one PR**, done when: domain tests cover the happy path and the "changes nothing on failure" case; the
Realm simulation is rerun where numbers move; new pages and actions have a playtest step; `game-design.md` and the
"What's built" list in `mageborn-design.md` are updated; and the privacy notice is updated where what's stored, or
who sees it, changes. **Sizes:** S a day or less, M a few days, L a week or more; an L item is split into PRs when
it starts. "Needs" names the items that must come first.

### Foundations
- [x] The design brought into the repo, decisions recorded, Skirmish removed
- [x] The schools renamed Dawn, Grove, Ember, Umbra and Veil, with the wheel order as data
- [x] **F1. Simulation baseline** (M): `tools/Spellbound.Balance` replays days 1–7 many times with bot strategies,
  with a baseline on today's rules, so every later item is measured
- [x] **F2. Ledger preview** (M): every action form shows its projected result before you confirm (acres gained,
  income after upkeep, what would disband), by running the action on a copy; on today's verbs first
- [ ] **F3. The Chronicle** (S): a public feed of every hostile act (attacker, target, result) from the action log
- [ ] **F4. Published net power** (S): the formula and its weights shown on Rivals; extended as Spell Level and
  creatures arrive

### Season and turns
- [ ] **S1. Turn bank** (S): 1 turn every 10 minutes, cap 150 (a day), 100 to start. Needs F1 (rerun)
- [ ] **S2. Season phases** (M): Sanctuary, Growth, Contest, Conjunction, Reckoning as **fractions of the round**, not
  days, so a 7-day Blitz alpha and a 28-day season both work; the Realm page shows the phase
- [ ] **S3. Explore decay** (S): yield falls with realm size and sharply from the Contest phase, and is better below
  the median. Needs S2
- [ ] **S4. Vigil** (S): freeze your realm once a season for up to 72 hours

### Economy
- [ ] **E1. Population and homesteads** (M): population as a resource; homesteads replace towns. Needs F2
- [ ] **E2. Workshops, forts and ward buildings** (M): build speed, your life, spell resistance. Needs E1.
  *Naming:* the research uses "Ward" for a building, a battle spell and the defence orders; decide one name each
  here
- [ ] **E3. Yield curves** (M): each building's yield per acre on a published curve, the sweet spot shaded on the
  Build page; batching is honest. Needs E2
- [ ] **E4. Channel** (S): a turn's worth of extra gold or mana income, replacing Meditate

### Magic
- [ ] **M1. Wheel costs** (S): no failure rolls; off-school magic costs +50% from a neighbour, +100% from an opposite
- [ ] **M2. Research** (L): the Study verb, libraries, four tiers, Spell Level. Needs E2 (libraries)
- [ ] **M3. Spells rebuilt** (L): enchantments with upkeep, sorcery, battle spells, one Apex per school. Needs M2.
  *Needs the owner:* spell names

### War
- [ ] **W1. Creatures** (L): per-school creatures and mundane troops, with class, initiative, damage type and one
  keyword. *Needs the owner:* creature names, and 5 stacks or 6–7
- [ ] **W2. Battle tournament** (M): a CLI that pits schools' armies against each other until no school's win rate
  strays; the research's second harness. Needs W1
- [ ] **W3. Stacked battles** (L): line-up, pairing, initiative, fatigue and counters; deterministic. Needs W1
- [ ] **W4. AI mages** (M): the simulation's bots play as live, clearly marked mages, one background job for all of
  them, so a small server has targets in range. Needs W3 (decided: right after battles). It's the first background
  job that acts for players: amend CLAUDE.md's background-job rule in the same PR
- [ ] **W5. War Table** (M): both line-ups, the pairing and an outcome band before you strike; scouting ±15%,
  blind to Wards. Needs W3
- [ ] **W6. Strike kinds** (M): Assault, Siege and Raid. Needs W3, E2 (forts)
- [ ] **W7. Limits** (M): range 75–150%, retaliation always in range, one hostile act per target per day, Shaken
  after 3 losses in 24 hours. Needs W6, F3
- [ ] **W8. Wards** (M): up to 3 "if the attacker is X% of me, cast Y" orders and a standing battle spell. Needs
  M3, W3
- [ ] **W9. Web push when struck** (S): a notification when you're attacked or a Ward fires. Needs W6

### The Fall and the season's end
- [ ] **D1. The Fall** (M): losing your last fort reincarnates you at half the median land, keeping school and
  research, with Sanctuary and then a Vengeance mark. Needs E2, M2
- [ ] **D2. Final War and titles** (M): the stars force the Final War at the end of the Conjunction; the Archmage,
  Sage, Conqueror and Unbroken titles in the hall of fame. Needs S2, M2, D1
- [ ] **D3. The product name** (S, *needs the owner*): "Mageborn" is the research's working title; decide the
  name, and the setting with it, before players see a Mageborn page

### Alpha with friends
- [ ] **A1. Mageborn alpha** with about 20 players, one Blitz-length season. Needs everything above. **The deploy
  prerequisites become blocking here** (M4: the VPS and domain, the Google OAuth client, the `PRIVACY_*` settings).
  Measure what the research names: the share of new players still active on day 7, how many find decoy stacks or the
  explore-to-war switch, the net power gap between rank 1 and the median late in the season, and how the Final War
  went

### After the alpha
- [ ] **L1. Armageddon** (M): five casts by five mages a day apart, Doomsayers attackable by anyone. Needs M2, D2
- [ ] **L2. Omens** (S): two per season, announced before day 1
- [ ] **L3. Guilds** (L): up to 8 members, one reinforcing stack, a guild title
- [ ] **L4. The Tutor's Plan and school labels** (M): a guided first 150 turns; Dawn and Grove marked
  beginner-friendly, Umbra and Veil hard
- [ ] **L5. Apprentice season** (M): 7 days against 20 AI mages. Needs W4

### Look and feel
The owner's art theme "The Burned Grimoire" ([theme.md](theme.md)), adopted 18 Oct 2026 and built before F2.
- [x] **T1. Theme, phase 1** (S): fonts, palette, cards, buttons, school inks
- [x] **T2. Theme, phase 2** (M): each mage's sigil (generated from their name), the turn moon, the mana vial, battle
  stamps, SVG filters
- [ ] **T3. Theme, later** (S): the rest of the motion, a light variant, and the rough card edges and worn titles if
  T4 finds the filters fast enough
- [ ] **T4. Checked on a real phone** (owner): blackletter readable at 360 px, SVG filters fast enough, glyphs render

## Engineering
- Persistence: plain Npgsql with Google sign-in, decided 17 Oct 2026 after comparing alternatives and similar games
  ([research](research/persistence.md))
- jsonb state: until the first deploy, the rework reshapes stored state freely and a dev database is reset (the
  school rename did this); from the first deploy on, a reshape comes with an upcaster
- Playtest tool (`tools/Spellbound.Playtest`): three players play every page at 360 px in a real browser; not in CI
  yet (it needs `playwright install --with-deps` there)
- Tests: most of their time is the app starting once per test; share one app per test class if speed matters

## Balance
See [balance.md](balance.md) and the open questions in [game design](game-design.md).
- Superseded by Mageborn: tiny attacks are limited by its hit limits (M7, W7), population is a resource (E1), and the
  season's end is the Conjunction (D2, L1). Numbers are tuned with the simulation and the alpha

## Later
- Items, heroes and gods (the research's possible expansions); chat; server variants (Mageborn's Blitz and Long
  servers)
- Real email (an SMTP service), if the game ever needs it
- More sign-in options (Apple, passkeys), linked to the same account
- Move `Hosting/` into its own reusable project if a separate game is ever planned (see architecture, "The game host
  boundary")
