# Spellbound roadmap

Rewritten for the clean-slate design in [game-design.md](game-design.md): a dark hex map, an opening draft, one
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
- [x] Cast is a stance again (owner's choice, 24 Oct 2026): one spell a Cast round, at the same costs, Rally removed; a Cast row on the orders form replaces the "Spell (optional)" fold; bots and the simulator
- [x] The stance draft, as in Puerto Rico (owner's choice, 25 Oct 2026): picks in turn, no stance twice a round, everyone
  takes each stance (the picker first, 1 mana less), a sixth stance Channel, Hold removed; turns in the domain
  (`Match.Take`), bots, the simulator, the table tool, the server's steps (one turn at a time), the orders form (pick,
  or take or pass) and a playtest that plays a round turn by turn; measured (`game-design.md`, Rounds and stances)
- [x] Board first and casual (owner's choice, 25 Oct 2026): seat tokens for whose turn, a stance tray under the map
  (picked tokens in their picker's colour, the stance going round pulsing, free ones glowing on your pick and dimmed
  otherwise), a bubble for what's next, and effects on the map as the turns play back (summon, attack, a tile taken,
  a spell); mock-ups in `tools/mockups/board-first.html`
- [x] Attune by a rolled price (owner's choice, 25 Oct 2026): any free tile, a base by realm size + 1 a step + a die;
  the price is shown, then paid or declined (`Match.Settle`), on the page as Pay / Decline; bots pay when they can
- [x] Barren land (owner's choice, 25 Oct 2026): a fourth tile type that yields nothing, only in the later Unveils and
  never guarded, still land for points and Attune prices; one per 2 players (`PlayersPerBarrenTile`), plain ash on the
  map until it has art (prompt in `tile-art.md`); measured in `game-design.md`, Tiles
- [x] The fog of war's mist over the dark tiles too (owner's choice, 25 Oct 2026), fainter than over a rival's hidden units
- [ ] Retune after the stance draft: the Warbrand's goal (maxed in 98% of games) and the point scale; play the bots'
  turns back one at a time on the board
  updated (unspent mana at the end rose from 11 to 28 a seat: a fix is proposed to the owner, not built)
- [x] Each trait gives a spell (owner's choice, 24 Oct 2026): one spell per trait, shared by two (Spellweaver all
  five); Ward, Blight and Lure go on several tiles in one Cast; Hexer counts rounds of Blight; the form, help page,
  bots, table tool and playtest follow
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
  - [x] 6a: new match (you and 2–4 bots), the match page with the hex map (inline SVG, tap a tile; at first the same choices as
    plain buttons, now the map alone), each seat in one of the theme's inks, the opening auction with its preview (`Match.CheckBid`), the
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
  choices, 21 Oct 2026): a bottom bar that jumps to Map, Orders, Report and Standings (since 24 Oct 2026, tabs between views: see below); the action (Commit, Bid and
  Pass, Choose) and its cost fixed above it in thumb reach; the stances as a 2×2 grid (later one a row); Moves and Spell folded behind
  a header that says what's in them; and a short report (your lines, your battles, where the match is) with
  "Everything that happened" a tap away. A match starts in three steps (owner's choice, 23 Oct 2026): a welcome
  (your seat, who's playing, how the match goes), the trait with the map still dark, then the map and the first
  auction; the standings wait for the rounds (nobody scores in the opening), then are a view of their own (with who leads), which opens by itself at
  the end. Tiles go by name, not coordinates (owner's choice, 23 Oct 2026): a row letter from the top and the place
  in the row ("C3"), shown on the selected hex (on every revealed hex until 24 Oct 2026, the owner's choice) and used in the lists, the orders and the reports. Each revealed hex
  shows its type's symbol (◆ Steady, ≈ Shifting, ✶ Wild), with a key under the map and the symbol before each tile in
  the lists; a summon asks what and how many before where. A held tile's one border is its edge in its owner's ink
  (heavier for yours), left out between two tiles of the same owner so each player's land reads as one zone (owner's
  choice, 24 Oct 2026; it was an inner ring inside the tile's own border), and the map shows painted art for the three types
  ([tile art](tile-art.md), generated with SDXL on a local ComfyUI, 24 Oct 2026). Shifting and Wild tiles roll their yield every round (owner's choice, 24 Oct 2026): the map shows each type's range, the round report lists the rolls. A round that begins while the match page is open plays once: a banner, "+N" rising from every held tile, and your mana's gain (owner's choice, 24 Oct 2026). The opening auctions go tile by tile: one tile per player, bid on in name order, the winner sits out the rest, the last tile goes free to whoever won none, and the 3 free tiles appear as the rounds begin (owner's choice, 24 Oct 2026; since replaced by the draft, below). In the opening, tiles go without names (the map marks the tile up now); names start with the rounds. The hexes show no ranges (the painted art and the key show the type); a held tile shows what it paid this round, small ("+3") (owner's choice, 24 Oct 2026). After each opening step the page plays it back a line at a time (1.5 s apart, with Skip): each pick (first each auction's bids), with the tile's border appearing then. The match page's top line holds only your colour, where the match is and your mana; the Help tab holds your trait's goal, the tile types, the opening, a round, scoring and every trait (owner's choice, 24 Oct 2026). The stances go one to a row, and the chosen one opens its own choices under it (a summon's units and how many, a build's upgrade, an attack's units and guard). Every tile is chosen on the map, with a line saying what to tap and the usable tiles marked: a stance's target, a move's from and to, a spell's tile, Leyline's second tile and where reinforcements guard; the lists and drop-downs are gone (owner's choice, 24 Oct 2026). The match page is three views, switched by the bottom tabs (owner's choices, 24 Oct 2026): Board (the map and the orders), Report (what just happened), Standings and Help, with the top line on each. The view is in the URL (#board, #report, #standings, #help), so back, reload and links keep it; half-given orders survive a look at another view; a report that comes in while the page is open puts a dot on the Report tab until it is read
- [x] Page looks and a raised board ([theme](theme.md), owner's choice, 24 Oct 2026): five backgrounds to swap
  between on the account page (Burned grimoire, Sigil night, Candlelit table, Arcane dust, Painted rune-slate), kept
  per device in a cookie, and the map's tiles raised like board-game pieces (a thickness and a bevel)
- [x] PWA manifest and icons so the game can be added to the home screen *(already built)*
- [x] Integration test for a round (WebApplicationFactory on a real PostgreSQL, like the platform's tests):
  `RoundFlowTests` commits a round and reads the match page back over HTTP, checks that failed orders change nothing
  and that a double tap waits on the row lock, ends a whole match for the host, and keeps the page to its player
- [x] AI players: `tools/Spellbound.Table` lets Claude Code agents with a personality play seats from the command line
  (on the owner's subscription, no API key); the first session's findings are in `playtests/2026-10-21-ai-players.md`
- [x] Upgrades (Shrine, Tower, Sanctum) with the Build stance, from the AI tastes' feedback: rules, bots, simulator,
  the orders form and its playtest step
- [x] Strategy bots (`--plans` in the simulator) and a retune so more plans pay off: Attune 1 + 1 per tile to 3 tiles then + 3 (after the second AI-taste session), Blight not on the same tile two rounds running, 2 points
  per conquest, mana produced 1 per 10, no points for unspent mana (`discussions/strategies.md`)
- [x] Reading rivals (`discussions/reading-rivals.md`), one at a time, asking the owner after each:
  - [x] Rally, the attacker's Ward (+2 power for 3 mana)
  - [x] traits secret until the end (a guess at each rival's trait was built, then removed on 24 Oct 2026: the owner found it beside the point)
  - [x] fog of war: rivals' far-off units and their mana hidden, and each player's report shows only what they could see
- [x] An undefended capture scores 1 point, a defended one 2 (after the third AI-taste session: raids on empty tiles decided every match)
- [x] Reinforcements with an attack: up to 3 power summoned at home, 1 mana more per unit (after the fourth AI-taste session)
- [x] Follow orders (shared stances, from Puerto Rico), built and then removed after the sixth AI-taste session: 4 of 13 happened
- [x] Unpicked stances get cheaper (Puerto Rico's doubloons): Attune, Summon or Build nobody chose costs 1 less a round, up to 3;
  removed (owner's choice, 24 Oct 2026): the owner hadn't known it was there, and stances now cost the same every round
- [x] Playtest steps for the new pages (`tools/Spellbound.Playtest`): every page has one, the not-found ones included
- [x] The opening as a draft (owner's choice, 24 Oct 2026; it replaced the tile-by-tile auction): each Unveil's tiles
  are taken one each in turn, for free, the first seat rotating from one draft to the next; starting mana 4; a missed
  pick is a bot's after 24 hours (`Match.Draft`, `Bot.ChoosePick`, the match page's pick list)

## M2: Playing with friends
- [x] Invites and a lobby (2–5 seats, bots fill empty seats): an invite link, `/join/{code}`
- [x] Commit-and-wait rounds: commit, take back, see who has committed ([decisions](discussions/playing-with-friends.md))
- [x] Round timeout and holds for players who time out: 24 hours a step, then hold; two misses in a row and a bot plays
  the seat (the player keeps watching)
- [x] Notifications (a step resolved, the others are waiting on you, you were attacked, someone joined your invite,
  your match started, a bot took your seat), over the web push already built (`Hosting/Push.cs`)
- [x] Host hands a dropped player's seat to a bot

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
