# Spellbound roadmap

Rewritten for the clean-slate design in [game-design.md](game-design.md): a dark hex map, an opening mana auction, one
stance per round, Imps and Golems, 8 rounds, points banked as you go.

The old game (the Realm and its Mageborn rework) was removed on 18 Oct 2026 and is in the git history at `b171e46`.
The platform it ran on stays and is already built (see *Already built* below), so some items here are ticked or
adjusted to it: persistence is plain Npgsql on PostgreSQL with numbered SQL scripts (not EF Core), and integration
tests run on a real PostgreSQL (not in-memory SQLite). The new game plugs in as `SpellboundGame` (it replaced `PlaceholderGame` in M1).

## Decisions (19 Oct 2026)
- **M1's domain starts now**, alongside the paper playtest: the playtest changes numbers, which live in `GameRules`,
  not the shape.
- **A match is a host game:** each match is a row in the host's `games` table (mode, seats, state); the always-on
  round goes away. This changes `Hosting/` (`RoundKeeper`, the admin page's round controls) when persistence is built.
- **A match is stored as one jsonb snapshot**: it is small and changes once per round. Tables only if game history or
  ladders need queries later. *(Built with an explicit `MatchState` record instead of `DomainJson`'s private-field
  reflection, so the stored format is deliberate; see architecture.md, "Matches".)*
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
- [x] Domain: setup (starting mana, the trait draft: each player their own 2 of a pool of 10, none shared:
  game-design.md, Traits), bots that draft and lean towards their trait, and the simulator's win rate and goal points
  by trait
- [x] Domain: spells (five spells cast alongside the stance: game-design.md, Spells), bots that cast, and the
  simulator's mana numbers (unspent at the end, spent per stance and on spells, spells cast, Imps and Golems summoned)
- [x] Pages: a "Spell (optional)" section on the orders form (a spell, its target and how much, with the preview), and
  its playtest step
- [x] Domain: the opening (three Unveils, sealed auctions, random leftovers, tie rule)
- [x] Domain: a round's submission (one stance + moves) and its validation (`Orders`, `Match.Check`)
- [x] Domain: resolution: income, upkeep, moves, Attune (price and clashes), Summon, attacks and battles
  (`Match.Resolve`, `Battle`)
- [x] Domain: later Unveils in rounds 2 and 4, monster guards
- [x] Domain: banked scoring, last-tile protection, end of game
- [x] Domain unit tests, including a scripted full game that always resolves the same way
- [x] Bots: a simple bid rule and a simple stance rule, and a bot-vs-bot simulator that plays many seeded games and
  reports on the paper prototype's "watch for" list (`Bot`, `Simulation`, `tools/Spellbound.Simulator`)
- [x] Bind `GameRules` from configuration (`RulesSetup`, section `GameRules`; each match keeps the rules it started with)
- [x] Persistence: a match as one jsonb snapshot in the host's `games` row, through the host's `DbSession` (see
  Decisions) *(was "EF Core mapping for games, seats, tiles, units and submissions")* (`MatchState`, `MatchStore`)
- [x] The host runs matches started on demand instead of one always-on round (`RoundKeeper`, `IRoundLifecycle`, the
  home page), and the game replaces `PlaceholderGame` behind the host's hooks. The admin page lists running matches
  (round, players) with an "end now" per match (owner's choice, 19 Oct 2026)
- [ ] Pages: new game (pick a trait), the opening auction, the map with tap-to-select tiles, the stance buttons, the
  round report, the end screen. Each action shows its cost and result first: Attune's price, what a Summon's mana
  buys, both sides' power before an attack
  - [x] 6a: new match (you and 2–4 bots), the match page with the hex map (inline SVG, tap a tile; the same choices as
    plain buttons), each seat in one of the theme's inks, the opening auction with its preview (`Match.CheckBid`), the
    last report, standings and the end; the home page links your matches
  - [x] 6b: the rounds' orders (stance buttons, targets, how many units attack from each tile, `Match.Check` as the
    preview), the round report, the end screen
  - [x] 6c: moves (any number, each unit one step, before the stance), and reports in the player's words ("You win",
    "Bot 2 attunes")
  - [x] 6d: the trait pick before the first bid, your trait on the match page, rivals' traits once the rounds begin,
    and each goal in the end report
- [x] Battle report text in the art direction's tone ([theme](theme.md)): your battles end in a stamp (Conquered,
  Repelled, Thwarted, Held, Lost), and the report and tile labels say "you" and "Bot N"
- [x] Mobile layout pass at 360 px: bottom navigation, big buttons, no horizontal scroll. The match page (owner's
  choices, 21 Oct 2026): a bottom bar that jumps to Map, Orders, Report and Standings; the action (Commit, Bid and
  Pass, Choose) and its cost fixed above it in thumb reach; the stances as a 2×2 grid; Moves and Spell folded behind
  a header that says what's in them; and a short report (your lines, your battles, where the match is) with
  "Everything that happened" a tap away
- [x] PWA manifest and icons so the game can be added to the home screen *(already built)*
- [x] Integration test for a round (WebApplicationFactory on a real PostgreSQL, like the platform's tests):
  `RoundFlowTests` commits a round and reads the match page back over HTTP, checks that failed orders change nothing
  and that a double tap waits on the row lock, ends a whole match for the host, and keeps the page to its player
- [x] AI players: `tools/Spellbound.Table` lets Claude Code agents with a personality play seats from the command line
  (on the owner's subscription, no API key); the first session's findings are in `playtests/2026-10-21-ai-players.md`
- [x] Upgrades (Shrine, Tower, Sanctum) with the Build stance, from the AI tastes' feedback: rules, bots, simulator,
  the orders form and its playtest step
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
- The game host (`Hosting/`): games started on demand and ended at their end by `GameKeeper`, the admin page, bans, multi-account
  signals, rate limits, alerts, nightly backups with a tested restore, web push notifications
- The privacy notice, the art theme "The Burned Grimoire" ([theme](theme.md)), and the playtest tool
- Pieces of the old game that may come back, from `b171e46`: the action log, the sigil generator, the ledger preview
  (`DomainJson` and the balance simulation are replaced by `MatchState` and `Simulation`)

## Later
- Traits (full list), more stances, more unit kinds and tile types, defender bonuses, teams, a ranked ladder
- Real email (an SMTP service), if the game ever needs it
- More sign-in options (Apple, passkeys), linked to the same account
