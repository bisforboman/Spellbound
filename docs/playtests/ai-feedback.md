# AI feedback: what players of other games want from Spellbound

A running summary of what AI players with board-game tastes ([tastes](../../tools/Spellbound.Table/tastes.md))
liked, missed and would add. Each taste is a type of player: the games they love, what they enjoy and what bores
them. They learn Spellbound only from its rules, play a whole match the way that taste would, then review it against
the games they know. This is a source of ideas, not a verdict: an AI with a taste is a sketch of a player, not a
player. Add each new session's reviews below and update the themes.

## Sessions

| Date | Version | Tastes (rating out of 5) | Reviews |
|---|---|---|---|
| 21 Oct 2026 | after #75 (unspent mana scores, Blight 3, traits retuned) | Euro-gamer 3, Wargamer 3, 4X fan 3, Combo player 3, Bluffer 3, Casual player 3 | [2026-10-21-ai-tastes/](2026-10-21-ai-tastes/) |

Every taste gave the game 3 out of 5: "quick, clean, readable, with real tension in the secret simultaneous orders",
but each one found it missing the thing its favourite games are about.

## What they liked (most tastes)

- **Secret, simultaneous orders.** Everyone liked guessing which tile a rival would hit, and every taste had a
  "gotcha" moment. The Bluffer, Casual player and Wargamer called it the best part.
- **`check` and the ledger.** Players always knew what an order would cost and do (Euro, 4X, Casual).
- **Banked points** that can't be taken away, and the Attune price curve: every expansion is a real sum (Euro).
- **Small combos:** Lure, then a lone Golem that a loss of 2 can't kill (4X, Combo); summon, then Leyline (Euro);
  Scorch, then attack (Combo, Wargamer).
- **Unveils as exploration** (4X, Casual).

## What they missed

Each theme lists who asked for it and the mechanics they borrowed.

### 1. Something to build that grows (Euro, 4X, Combo: 3 of 6)

"Nothing compounds." "Round 8 felt like round 1 with more mana." "Nothing I buy in round 2 makes round 6 better."

All three asked, separately, for **upgrades you build on a tile**:
- a Shrine that raises a tile's yield or makes it Steady (Euro, from Terraforming Mars' production tracks);
- a tower or shrine that raises yield or defence (4X, from Civilization's buildings);
- a Ley Shrine that makes spells cast next to it cheaper (Combo, from Dominion's engine cards).

Related wishes:
- a research track: Golem power 4, a second spell a round, a cheaper Attune (4X, from Eclipse);
- converters that turn mana into point cards once a round (Euro, from Agricola's improvements);
- adjacency bonuses for linked tiles of one type (Euro, from Brass);
- trait thresholds: after your 2nd Blight, Blights also drain mana (Combo, from Spirit Island).

### 2. Bigger scoring moments and goals to race for (4X, Casual, Wargamer: 3 of 6)

"Points come in drips." "I never felt a clear 'I just scored!' moment."

- public objectives revealed each round (4X, from Twilight Imperium);
- completing a region or a ring of tiles (Casual, from Ticket to Ride's routes and Carcassonne's closed cities);
- region bonuses for holding a connected cluster (Wargamer, from Risk's continents).

### 3. Talking, deals and bluffing on purpose (Bluffer, Casual: 2 of 6)

"Nothing I said or did could shift anyone else's choices." "Nothing passes between players except attacks."

- a public pact or threat line each round, not binding (Bluffer, from Diplomacy);
- trading mana for a promise (Casual, from Catan);
- traits kept secret until scored, with a reward for guessing a rival's (Bluffer, from Coup).

### 4. Fights decided by more than a summon after the fact (Wargamer, Bluffer: 2 of 6, and every earlier session)

"An empty tile defends almost as well as a garrisoned one." The defender can summon a big stack on the attacked
tile in the same round, and a failed attack is a pure loss.

- limit summons: only on tiles with no enemy next to them, or only on a home tile, so armies are placed before the
  fight (Wargamer, from Kemet);
- face-down combat cards or hidden reinforcements committed by both sides (Bluffer, from Cosmic Encounter);
- points for fighting, win or lose (Wargamer, from Blood Rage's glory);
- one reaction a round, such as a hidden ward that triggers when attacked (Combo, from Magic's instants and
  Dominion's Moat).

### 5. Smaller points

- **The map is small:** nothing was left to attune by round 5 (4X). The 4X fan wanted more Unveil rings.
- **Monsters can be missing:** Beastbane scored 0 when the Unveils revealed no monster tile (Wargamer), or capped
  at 1 with one monster on the map (Bluffer).
- **One stance a round makes growth and defence compete:** "about 4 meaningful actions in 8 rounds" (4X, Euro).
- **Golems:** upkeep makes Imps better for strikes (Wargamer; also earlier sessions).
- **Seeing the map:** the Casual player read the board as a list of coordinates. That is the AI tool's text view;
  the game's page draws the hex map. But "chunkier", more visible scoring is a fair point for the page too.

## Candidate additions, by how many tastes they'd serve

| Addition | Serves | Size |
|---|---|---|
| Tile upgrades (a shrine or tower: yield, defence or cheaper spells) | Euro, 4X, Combo (and gives late mana a use) | medium: one new action or spell, three upgrade kinds |
| Public objectives or region goals (shared targets that pay a chunk) | 4X, Casual, Wargamer | medium: a small deck of goals, drawn from the seed |
| Summon limits near enemies (armies placed before the fight) | Wargamer, Bluffer, and every earlier session's "defenders summon after the fact" | small: one rule |
| Pacts and messages (non-binding) | Bluffer, Casual | larger: a page feature, matters only with people |
| Hidden combat commitments (face-down reinforcements) | Bluffer, Combo | medium |
| A research track | 4X, Combo | larger |
