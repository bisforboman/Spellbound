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
- **Length:** a set number of rounds (12 by default), or earlier if one mage holds half the board.
- **Round time limit:** set by the creator, for example 12 hours (a game over a week) or 2 minutes (a live game with
  friends). A round ends as soon as everyone is ready.

## A round

1. **Plan.** Each player has **3 actions** per round (a switch) and chooses them in private. Orders can be changed
   until the player taps Ready, and Ready can be taken back until the round plays.
2. **Reminders.** When everyone else is ready, the last players get a nudge. Everyone who isn't ready gets one when an
   hour (or a tenth of a short limit) is left.
3. **Play out.** When all are ready or time runs out, the server plays the round (a background job per game, the kind
   of global event the architecture allows). Players who didn't finish keep the orders they had saved.
4. **Report.** Everyone reads a short report of what happened, in order, then plans the next round.

### Orders

| Order | Actions | Effect |
|---|---|---|
| **Claim** | 1 | Take a free region next to one you held when the round began, then draw a discovery card |
| **Build** | 1 | Build 1–2 nodes or towns in free slots of one region (300 gold each); a region claimed earlier in the same round counts |
| **Recruit** | 1 | Recruit 1–20 units of one kind (the Realm's units and prices) |
| **Cast** | 1 | A self-spell or a hex, as in the Realm, lasting a number of rounds |
| **Attack** | 2 | Attack a neighbouring region of another mage; a win takes the region and its buildings |

Income comes once per round, after the orders: 100 gold per region, 60 per town (×1.2 on Terrain 2) and 50 mana per
node (×1.5 on Terrain 3). Then unit upkeep (the Realm's per-turn upkeep, once a round); if a mage can't pay, its gold
goes to 0 and 10% of every unit deserts.

**Checking orders.** Saved orders are checked as the round would play them if nobody else got in the way: claims first,
then builds and recruits against a running gold total, all within the round's actions. When the round plays, each order
is checked again: one the round made impossible (another mage won the region) is spent and reported, and nothing else
changes. Orders past the round's actions are not done.

### Order of play

Everything ordered in a round happens in a fixed order, so simultaneous moves have clear outcomes:

1. **Casts**, so this round's barriers and buffs count.
2. **Attacks**, all at once against the defences at the start of the round. A region attacked by two mages goes to the
   stronger winning attack.
3. **Claims.** Several mages claiming the same free region: a contest, settled by a roll weighted by 10 plus each
   one's attack power, so an army helps but never decides alone (shown as a percentage before you commit, like all
   odds). The loser's action is spent; only the winner draws a card.
4. **Builds and recruits.**
5. **Income and upkeep.**

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
- 1 per building
- 2 per battle won
- **Sets:** +5 for holding one of each terrain
- **Objectives:** 3 public goals drawn per game (for example "first to 6 regions", "most nodes", "win 3 battles"),
  worth 5 points each to whoever gets there first
- Card 4s from the deck

Ties go to the most gold and mana together, then to the earlier seat. The winner's account gets a win on the Skirmish leaderboard; everyone who
finished gets a game played.

## What the simulation says so far (part A)

`tools/Spellbound.SkirmishSim` plays 1,000 four-seat games with bots ([results](skirmish-sim-results.md)). With part A's
rules (no attacks yet):

- **Claiming wins.** A region is worth 3 points, a building 1 point for 300 gold, so the bot that claims everything it
  can wins most games and the one that builds instead almost never does.
- **Games aren't open yet:** the leader after round 6 usually wins, because the board is full by round 8 or so and
  nothing changes hands after that. Attacks (part B) are what should change it.
- **Gold piles up** once every slot is built, and mana has nothing to buy. Part B's spells and hexes are priced from
  these numbers.
- Seat order doesn't matter (each seat wins about a quarter), and contested claims happen about 3 times a game.

Tuning waits for part B: attacks, spells and hexes change every one of these.

## Dropouts

A mage who wasn't ready in **2 rounds in a row** is marked away: their regions defend at half strength until they play
again. After 4 rounds away, their regions turn neutral (free to claim) and they score nothing.

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

## Open questions

- Is 3 actions per round right, and should some orders (attack) cost more?
- Should the board and other players' armies be fully visible, or partly hidden (scouting as an order)?
- Can players talk in a game (a chat), or is that *later*?
