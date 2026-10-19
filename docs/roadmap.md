# Spellbound roadmap

Rewritten for the clean-slate design in [game-design.md](game-design.md): a dark hex map, an opening mana auction, one
stance per round, Imps and Golems, 8 rounds, points banked as you go.

The old game (the Realm and its Mageborn rework) was removed on 18 Oct 2026 and is in the git history at `b171e46`.
The platform it ran on stays and is already built (see *Already built* below), so some items here are ticked or
adjusted to it: persistence is plain Npgsql on PostgreSQL with numbered SQL scripts (not EF Core), and integration
tests run on a real PostgreSQL (not in-memory SQLite). `PlaceholderGame` holds the game's place until M1 replaces it.

## Decisions (19 Oct 2026)
- **M1's domain starts now**, alongside the paper playtest: the playtest changes numbers, which live in `GameRules`,
  not the shape.
- **A match is a host game:** each match is a row in the host's `games` table (mode, seats, state); the always-on
  round goes away. This changes `Hosting/` (`RoundKeeper`, the admin page's round controls) when persistence is built.
- **A match is stored as one jsonb snapshot** (bringing back `DomainJson` from `b171e46`): it is small and changes
  once per round. Tables only if game history or ladders need queries later.
- M1 is synchronous: bots commit at once, so commit-and-wait, timeouts and timers wait for M2.

## M0: Bootstrap and paper test
- [x] Design draft, architecture notes, CLAUDE.md
- [x] ~~Domain rules for the turn-bank economy~~ *superseded by the clean-slate design*
- [x] Run `scripts/bootstrap.ps1`, make the build and tests pass, then make the first commit *(done long ago; the
  platform builds and tests green)*
- [ ] Paper playtest with [skirmish-paper-prototype.md](skirmish-paper-prototype.md); check the first-guess numbers
  (points, monster strength, prices)

## M1: A game against bots
Goal: on a phone, start a 3-player game against 2 bots and play all 8 rounds.

- [x] Domain: hex map (19 or 37 tiles), tile types and yields fixed at Unveil, all from a seed (`Hex`, `GameMap`,
  `GameRules`, `SeededRandom`)
- [ ] Domain: setup (starting mana, trait draft of the same 3 for everyone); starting mana is in, the trait draft waits
  for the traits themselves
- [x] Domain: the opening (three Unveils, sealed auctions, random leftovers, tie rule)
- [x] Domain: a round's submission (one stance + moves) and its validation (`Orders`, `Match.Check`)
- [x] Domain: resolution: income, upkeep, moves, Attune (price and clashes), Summon, attacks and battles
  (`Match.Resolve`, `Battle`)
- [x] Domain: later Unveils in rounds 2 and 4, monster guards
- [x] Domain: banked scoring, last-tile protection, end of game
- [x] Domain unit tests, including a scripted full game that always resolves the same way
- [ ] Bots: a simple bid rule and a simple stance rule, and a bot-vs-bot simulator that plays many seeded games and
  reports on the paper prototype's "watch for" list
- [ ] Bind `GameRules` from configuration (`TimeProvider.System` is already registered)
- [ ] Persistence: a match as one jsonb snapshot in the host's `games` row, through the host's `DbSession` (see
  Decisions) *(was "EF Core mapping for games, seats, tiles, units and submissions")*; the game replaces
  `PlaceholderGame` behind the host's hooks, which change from one always-on round to matches started on demand
- [ ] Pages: new game (pick a trait), the opening auction, the map with tap-to-select tiles, the stance buttons, the
  round report, the end screen. Each action shows its cost and result first: Attune's price, what a Summon's mana
  buys, both sides' power before an attack
- [ ] Battle report text in the art direction's tone ([theme](theme.md))
- [ ] Mobile layout pass at 360 px: bottom navigation, big buttons, no horizontal scroll *(the layout and theme exist;
  the game's pages need the pass)*
- [x] PWA manifest and icons so the game can be added to the home screen *(already built)*
- [ ] Integration test for a round (WebApplicationFactory on a real PostgreSQL, like the platform's tests)
- [ ] Playtest steps for the new pages (`tools/Spellbound.Playtest`)

## M2: Playing with friends
- [ ] Invites and a lobby (2–5 seats, bots fill empty seats)
- [ ] Commit-and-wait rounds: commit, take back, see who has committed
- [ ] Optional round timeout and hold rounds for players who time out
- [ ] Nudges and notifications (round resolved, your turn to commit, you were attacked); web push is already built
  (`Hosting/Push.cs`), so this is wiring it to the game's events
- [ ] Host hands a dropped player's seat to a bot

## M3: Hosting
Most of this was built for the old game and stays:
- [x] Accounts (Google sign-in, data download, account deletion) *(already built)*; game history is still to do
- [ ] Game history: a player's finished games
- [x] Dockerfile and docker-compose with Caddy ([deploy guide](deploy.md)) *(already built)*; deploying to a VPS waits
  on the owner (below)
- [x] Backups, basic rate limiting and anti-multi-account checks *(already built)*; Cloudflare waits on the domain
- [x] Admin page: ban, round control, problems *(already built)*; "end a stuck game" is still to do for the new game
  shape

### Waiting on the owner (before any deploy)
- [ ] A VPS and a domain pointing at it (see [deploy guide](deploy.md))
- [ ] A Google OAuth client for sign-in (see [deploy guide](deploy.md))
- [ ] Read the privacy notice (`/privacy`) and fill in its `PRIVACY_*` settings, and update it for what the new game
  stores (see [deploy guide](deploy.md#privacy-notice))
- [ ] Terms and rules page, and an "ask an admin" contact path (the server sends no email)

## Already built: the platform
- Blazor Server on .NET 10, PostgreSQL through plain Npgsql, numbered SQL migrations ([architecture](architecture.md))
- The game host (`Hosting/`): games and rounds kept running by `RoundKeeper`, the admin page, bans, multi-account
  signals, rate limits, alerts, nightly backups with a tested restore, web push notifications
- The privacy notice, the art theme "The Burned Grimoire" ([theme](theme.md)), and the playtest tool
- Pieces of the old game that may come back, from `b171e46`: `DomainJson` (jsonb snapshots of domain objects), the
  action log, the balance simulation, the sigil generator, the ledger preview

## Later
- Traits (full list), spells, more stances, more unit kinds and tile types, defender bonuses, teams, a ranked ladder
- Real email (an SMTP service), if the game ever needs it
- More sign-in options (Apple, passkeys), linked to the same account
