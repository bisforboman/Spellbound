# Tile upgrades (proposed 21 Oct 2026)

Why: in the AI feedback session (`../playtests/ai-feedback.md`), three of six tastes asked for this on their own:
- the Euro-gamer: "nothing compounds";
- the 4X fan: "round 8 felt like round 1 with more mana";
- the Combo player: "nothing I buy in round 2 makes round 6 better".

All three asked for something built on a tile that keeps paying. Upgrades would also give late-game mana a use: both
earlier AI sessions ended with 27 to 67 mana unspent.

## The proposal

At most one upgrade per tile. Three kinds, one per wish:

| Upgrade | Asked for by | Effect, for as long as it stands |
|---|---|---|
| **Shrine** | Euro, 4X | The tile yields 1 more mana. |
| **Tower** | 4X (defence), and it answers every session's "an empty tile is free to take" | The tile defends with 2 more power, even with no units on it. |
| **Sanctum** | Combo | Your spells aimed at this tile or a tile next to it cost 1 less. |

## Decisions for the owner

### 1. How a build is committed

| Option | For | Against |
|---|---|---|
| **A. Alongside the stance**, like the spell: optional, at most one a round, paid on top | Growth stops competing with the stance (the 4X fan and the Euro-gamer complained that one stance a round makes growth and defence compete) | The orders form gets a fourth section, which needs the same fold as Moves and Spell |
| **B. Shares the spell's slot**: a spell or a build each round | Cheap to build, and limits itself | Pits the Sanctum against casting, and gives rich players little more to do |
| **C. A Build stance** | Simple | Deepens the "one stance" complaint |

Recommended: **A**.

### 2. The rule against snowballing

`game-design.md` says no trait changes income, prices or costs, because that snowballs (principles 12 and 13: everyone
stays in the game, and the biggest realm pays the most to grow). A Shrine changes income and a Sanctum changes costs.
`design-principles.md` is referred to but is missing from the repository, so the principles here are reconstructed
from the design document.

Three ways to keep them in check:
- **A rising price**, like the Attune price: 5 mana plus 1 for every upgrade you already hold. The biggest builder
  pays the most.
- **A cap**: at most 3 upgrades per realm.
- **Leave out the Shrine and the Sanctum**, and keep only the Tower plus a point for each upgrade.

Recommended: **the rising price**, all three kinds.

### 3. When the tile is taken

- **It passes with the tile.** Conquest gets richer: the Wargamer asked for "big pushes to pay off big". This fits
  "Blight follows the tile".
- **It's destroyed.** This protects the builder, but a captured upgrade is lost to everyone.

Recommended: **it passes with the tile**. It also makes the Tower worth having.

### 4. What an upgrade is worth at the end

A build has to beat sitting on mana, which now scores 1 point per 10. Example: a Shrine built in round 3 for 5 mana
returns about 5 mana by the end, which only breaks even.

Recommended: **each upgrade you hold at the end is worth 1 point**. A first build (5 mana) then beats saving those 5
mana, and the rising price keeps the 4th and 5th builds a real choice.

## How we'd check it

- **Bots:** they build when they'd keep 8 or more mana after the stance:
  - a Shrine on their best-yielding tile;
  - a Tower on a threatened border tile;
  - a Sanctum when they cast often.
- **The simulator** reports builds per game and mana spent on them, and win rates as before.
- **Then the AI tastes run again**, the Euro-gamer, 4X fan and Combo player first, to see whether they find the arc
  they asked for.
