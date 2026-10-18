# Skirmish design (draft proposal)

A first proposal to react to, not decided rules. Names are placeholders (see
[Names to decide](game-design.md#names-to-decide)). Skirmish is the board-game mode of Spellbound (see
[game design](game-design.md#modes-decided-18-oct-2026-not-built-yet)): a few mages on one small, shared board, played in
**rounds that everyone plays at the same time**, with a clear end and a score. It shares the core with the Realm (mages,
schools, spells, armies) and changes the land and the clock. Version 1 is kept deliberately small; everything marked
*later* waits until v1 is fun.

## In one paragraph

Four mages start on a board of about sixteen named regions. Each round, every player privately plans their moves
(claim a neighbouring region, build, recruit, attack, cast) and taps **Ready**. When everyone is ready, or the round's
time runs out, all the moves play out at once and everyone reads what happened. Claiming a region draws a card from a
shared, visible deck: maybe ruins full of gold, maybe a mana spring, maybe nothing. After a set number of rounds the
game ends and points are counted from several sources, so there's more than one way to win.

## Decided so far (18 Oct 2026)

- **Simultaneous rounds with a ready-up**, not regenerating turns. A round has a time limit; players get reminders when
  everyone else is ready and when time is running out.
- **Dropouts:** a mage who misses rounds gets easier to take (see Dropouts).
- **Several games at once:** one Realm mage plus a few Skirmish games (for example up to 3), each with its own mage.
- **Results:** a simple win record per account (games played and won), shown on a leaderboard.
- **Joining:** public games are listed for anyone; private ones are joined with a code or link. 2 players is enough.

## The board

- **Regions, not a grid.** Each region has a name, a terrain and a list of neighbours. It's shown as text: your regions,
  then the ones next to them, so it works at 360 px. No map picture in v1.
- **Size:** about 4 regions per player (16 for 4 players), generated from the game's seed, so every game's board is new
  and a game can be replayed.
- **Terrain** decides what a region is good for:

  | Terrain | Effect |
  |---|---|
  | Terrain 1 | +1 building slot |
  | Terrain 2 | Gold income ×1.2 from its buildings |
  | Terrain 3 | Mana income ×1.5 from its nodes |
  | Terrain 4 | Defence ×1.5 when attacked |

- **Building slots:** each region holds a fixed number of buildings (for example 4), so land is limited and every
  building choice counts. Nodes and towns work as in the Realm.

## A game

- **Players:** 2 to 6, chosen by the creator (4 by default). The game starts when it's full, or at a set time with
  whoever has joined.
- **Start:** each mage gets one home region (Terrain 1 for everyone, spread out on the board, never next to another
  home) with one node and one town, 1,500 gold and 1,000 mana.
- **Length:** a set number of rounds (12 by default), or earlier if one mage holds three quarters of the board.
- **Round time limit:** set by the creator, for example 12 hours (a game over a week) or 2 minutes (a live game with
  friends). A round ends as soon as everyone is ready.

## A round

1. **Plan.** Each player has **3 actions** per round (a switch) and chooses them in private. Orders can be changed
   until the player taps Ready, and Ready can be taken back until the round plays.
2. **Reminders** (built, by web push, for players who turned notifications on): when everyone else is ready, the last
   player gets a nudge (once a round). Everyone who isn't ready gets one when an hour (or a tenth of a short limit) is
   left. When a round plays, everyone hears, except the player whose Ready played it.
3. **Play out.** When all are ready or time runs out, the server plays the round (a background job per game, the kind
   of global event the architecture allows). Players who didn't finish keep the orders they had saved.
4. **Report.** Everyone reads a short report of what happened, in order, then plans the next round.

### Orders

| Order | Actions | Effect |
|---|---|---|
| **Claim** | 1 | Take a free region next to one you held when the round began, then draw a discovery card |
| **Build** | 1 | Build 1–2 nodes or towns in free slots of one region (300 gold each); a region claimed earlier in the same round counts |
| **Recruit** | 1 | Recruit 1–20 units of one kind (the Realm's units and prices) |
| **Cast** | 1 | One of your school's self-spells or the ward (see Spells and hexes) |
| **Hex** | 1 | Your school's hex at another mage (see Spells and hexes) |
| **Attack** | 2 | Attack a region of another mage next to one of yours; a win takes the region and its buildings |

Income comes once per round, after the orders: 100 gold per region, 60 per town (×1.2 on Terrain 2) and 50 mana per
node (×1.5 on Terrain 3). Then unit upkeep (the Realm's per-turn upkeep, once a round); if a mage can't pay, its gold
goes to 0 and 10% of every unit deserts.

**Checking orders.** Saved orders are checked as the round would play them if nobody else got in the way: claims first,
then builds, recruits, spells and hexes against running gold and mana totals, all within the round's actions. When the round plays, each order
is checked again: one the round made impossible (another mage won the region) is spent and reported, and nothing else
changes. Orders past the round's actions are not done.

### Order of play

Everything ordered in a round happens in a fixed order, so simultaneous moves have clear outcomes:

1. **Self-spells**, so this round's wards and buffs count, **then hexes**, seat by seat.
2. **Attacks**, all at once against the defences at the start of the round. A region attacked by two mages goes to the
   stronger winning attack. Losses are taken after every battle.
3. **Claims.** Several mages claiming the same free region: a contest, settled by a roll weighted by 10 plus each
   one's attack power, so an army helps but never decides alone (shown as a percentage before you commit, like all
   odds). The loser's action is spent; only the winner draws a card.
4. **Builds and recruits.**
5. **Income and upkeep**, then running spells count down, then objectives, then the end check.

### Attacks

The Realm's formula, per region: the attacker's army attack (×1.15 if its school is strong against the defender's,
×0.85 if weak) × luck (0.9–1.1), against the region's defense × luck. A region's defense is the holder's army defense
plus 20 for the region's own guards and **15 per building**, ×1.5 on Terrain 4, and halved while the holder is away.
Both are shown before you commit. The attacker loses 8% of its units on a win and 12% on a loss; the defender 10% and
5%. A taken region keeps its buildings.

**Range:** you can only attack a mage holding at least **60%** as many regions as you (the Realm's range rule), so a
leader can't pick off the smallest and nobody is wiped out. Switches, off by default: safe homes (a home can't be
attacked) and capture damage (a share of a taken region's buildings is destroyed).

## Spells and hexes

The Realm's spell book, with a Skirmish meaning for each. Every effect is a placeholder like its name, decided in code
for the beta:

| School | Self-spell (400 mana, 3 rounds) | Hex (500 mana) |
|---|---|---|
| A | Gold income ×1.25 | 10% of their units die |
| B | Your weight in contested claims ×1.5 | 15% of their gold is destroyed |
| C | Mana income ×1.3 | Take 15% of their mana |
| D | Buildings cost ×0.8 gold | 1 building in their most built region is destroyed |
| E | One more action next round (once) | They have 1 action fewer next round |

The **ward** (any school, 300 mana, 3 rounds) halves the chance of hexes against you. A hex lands 70% of the time,
±15 points by the school ring, times the target's ward; the chance is shown before you commit, and the mana is spent
either way.

## Objectives

Three public goals are drawn for each game from: hold 6 regions, have 12 buildings, win 3 battles, have 5 nodes, have
40 units, hold one region of every terrain. Whoever reaches one first scores 5 points; if several reach it in the same
round, they all do.

## Luck: the discovery deck (v1's one luck feature)

Claiming a region draws a card from a shared deck of about 30 cards, shuffled from the game's seed. Everyone can see
**what's left in the deck**, so you know the odds before you claim (luck before the decision):

| Card | Count | Effect |
|---|---|---|
| Card 1 | 12 | Nothing: just land |
| Card 2 | 6 | +600 gold |
| Card 3 | 5 | +400 mana |
| Card 4 | 3 | +3 points |
| Card 5 | 4 | Lose 10% of every unit type (rounded up), or nothing if you have none |

Battle luck (×0.9–1.1, as in the Realm) stays, with the odds shown before you attack.

## Scoring

Points from several sources, shown live on the game page:

- 3 per region, +2 per Terrain 3 or Terrain 4
- 2 per building
- 2 per battle won (a region taken)
- **Sets:** +5 for holding one of each terrain
- **Objectives:** 3 public goals drawn per game (for example "first to 6 regions", "most nodes", "win 3 battles"),
  worth 5 points each to whoever gets there first
- Card 4s from the deck

Ties go to the most gold and mana together, then to the earlier seat. The winner's account gets a win on the Skirmish leaderboard; everyone who
finished gets a game played.

## What the simulation says

`tools/Spellbound.SkirmishSim` plays 1,000 four-seat games per scenario with bots ([results](skirmish-sim-results.md)).
The defaults above came from it:

- **Part A alone** (no attacks): claiming won almost every game and the leader at half time nearly always won, because
  the board fills by round 8 and nothing changes hands after.
- **Attacks without limits** made the fighter win 83% and ended games by round 5; leaders snowballed (more regions,
  more gold, bigger army) and most games wiped a seat off the board.
- **What fixed most of it:** the range rule (the biggest change: no wipe-outs, median winning margin from 34 to 11
  points, a fifth of games won by 3 points or less), buildings that defend and are worth 2 points, and ending early
  only at three quarters of the board. Every game now runs its 12 rounds.
- **Still open:** one strategy (claim everything) wins about two thirds of games, and luck decides few of them. But the
  bots attack only sure wins and never gang up on a leader, as people would, so they can't say whether the game is
  open. **The friends beta tunes it**; the simulator keeps catching broken rules (wipe-outs, unused resources, games
  that end early).

## Dropouts

A mage who wasn't ready in **2 rounds in a row** is marked away: their regions defend at half strength until they play
again (the orders they saved still play). After **6 rounds in a row** they're out: their regions turn free to claim
(buildings and all), they score nothing and can't be targeted.

## Switches (set by the game's creator)

Players (2–6), rounds (6–20), round time limit, actions per round, attacks on/off, hexes on/off, discovery deck on/off.

## What this needs from the rest of the app

- **Reminders reach the phone.** The server sends no email, so this means **web push** (the game is already an
  installable web app). It's in "Later" today and moves into Skirmish v1. It may need a NuGet package that isn't
  Microsoft's, to be asked for when it's built.
- **Orders, not instant actions.** In the Realm an action happens when you tap. In Skirmish you save orders and they
  happen when the round plays, so the domain gets a "play a round" step that takes every player's orders and a seeded
  random source, and returns the new board and a report per player. It's deterministic and replayable like the rest.

## Later

Trade between players, world events, a starting draft (pick 1 of 3 boons), a drawn map picture, alliances, a skill
rating.

## What's built

The rules (in the domain), storage, a service that plays rounds when everyone is ready or the time limit passes, and
the game page (`/skirmish/{id}`): orders with their cost and odds, Ready, the last round's report, players and scores,
the board as text, objectives and the deck. It refreshes itself every few seconds.
For now **the admin starts games** from the players' emails on the admin page; players use their Realm mage's name
and school when they have one. Players creating and joining their own games (with invite codes) comes next, after the
game page. Saved orders can't change while the player is ready: they take back Ready first.

**What players see of each other:** regions, buildings, armies, points and who's ready (attack and hex odds depend
on them), but not each other's gold, mana or orders. The deck shows how many of each card are left, never their order.
A contested claim's odds depend on who else claims, which nobody knows in advance, so the page shows a player's own
weight and how it works, not a percentage.

## Decided in code, to review in the beta

The design left these open; part B picked something so the game can be played:
- what each school's spell and hex does in Skirmish (the table above), and their mana prices;
- that a taken region keeps its buildings, and buildings add 15 defense;
- away after 2 missed rounds, out after 6;
- the objective list and thresholds;
- the range rule at 60%, and ending early at 75% of the board.

## Open questions

- Is 3 actions per round right, and should some orders (attack) cost more?
- Should the board and other players' armies be fully visible, or partly hidden (scouting as an order)?
- Can players talk in a game (a chat), or is that *later*?
