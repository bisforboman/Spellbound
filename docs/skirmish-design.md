# Skirmish design (draft proposal)

A first proposal to react to, not decided rules. Skirmish is the board-game mode of Spellbound (see
[game design](game-design.md#modes-decided-18-oct-2026-not-built-yet)): a few mages on one small, shared board, a clear
end and a score. It shares the core with the Realm (mages, schools, spells, turns, armies) and changes the land.
Version 1 is kept deliberately small; everything marked *later* waits until v1 is fun.

## In one paragraph

Four mages start on a board of about sixteen named regions. You spend turns to claim neighbouring regions, build in
them and fight over them. Claiming a region draws a card from a shared, visible deck: maybe ruins full of gold, maybe a
mana spring, maybe nothing. After a few days the game ends and points are counted from several sources, so there's more
than one way to win.

## The board

- **Regions, not a grid.** Each region has a name, a terrain and a list of neighbours. It's shown as text: your regions,
  then the ones next to them, so it works at 360 px. No map picture in v1.
- **Size:** about 4 regions per player (16 for 4 players), generated from the game's seed, so every game's board is new
  and a game can be replayed.
- **Terrain** decides what a region is good for:

  | Terrain | Effect |
  |---|---|
  | Plains | +1 building slot |
  | Forest | Gold income ×1.2 from its buildings |
  | Ley line | Mana income ×1.5 from its nodes |
  | Mountain | Defence ×1.5 when attacked |

- **Building slots:** each region holds a fixed number of buildings (for example 4), so land is limited and every
  building choice counts. Nodes and towns work as in the Realm.

## A game

- **Players:** 3 to 6, chosen by the creator (4 by default). The game starts when it's full, or at a set time with
  whoever has joined.
- **Start:** each mage gets one home region (the same terrain for everyone, spread out on the board) and the Realm's
  starting gold and mana, scaled down.
- **Turns:** they regenerate as in the Realm, but faster (for example 1 every 3 minutes, cap 120), so a game of a few
  days has enough decisions in it.
- **Length:** 3 days by default (a switch), or earlier if one mage holds half the board.

## Actions

| Action | Turns | Effect |
|---|---|---|
| **Claim** | 3 | Take a free region next to one of yours, then draw a discovery card |
| **Build** | 1 per building | Build a node or town in a free slot of one of your regions |
| **Meditate**, **Cast** | as in the Realm | Unchanged |
| **Recruit** | as in the Realm | Unchanged |
| **Attack** | 4 | Attack a neighbouring region of another mage; a win takes the whole region and its buildings |

## Luck: the discovery deck (v1's one luck feature)

Claiming a region draws a card from a shared deck of about 30 cards, shuffled from the game's seed. Everyone can see
**what's left in the deck**, so you know the odds before you claim (luck before the decision):

| Card | Count | Effect |
|---|---|---|
| Nothing | 12 | Just land |
| Ruins | 6 | +1,500 gold |
| Mana spring | 5 | +1,000 mana |
| Old shrine | 3 | +3 points |
| Lair | 4 | A monster: lose 10% of your army, or nothing if you have none |

## Scoring

Points from several sources, shown live on the game page:

- 3 per region, +2 per Ley line or Mountain
- 1 per building
- 2 per battle won
- **Sets:** +5 for holding one of each terrain
- **Objectives:** 3 public goals drawn per game (for example "first to 6 regions", "most nodes", "win 3 battles"),
  worth 5 points each to whoever gets there first
- Shrine cards from the deck

Ties go to the most gold and mana together.

## Switches (set by the game's creator)

Players (3–6), length (1–7 days), turn speed, attacks on/off, hexes on/off, discovery deck on/off.

## Later

Trade between players, world events, a starting draft (pick 1 of 3 boons), a drawn map picture, alliances.

## Open questions

- Should turns regenerate (like the Realm) or be a fixed budget per day, so nobody can out-play others by being online
  more?
- What happens to a mage who stops playing: do their regions stay, go neutral, or get easier to take?
- Can you be in several Skirmish games at once, and in the Realm too?
- Do Skirmish results count for anything outside the game (a rating, a hall of fame)?
