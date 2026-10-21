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
| 21 Oct 2026 | after #79 (upgrades; Attune 1 + 3 per tile; 2 points per conquest; mana 1 per 10; no points for unspent mana) | Euro-gamer 2, Wargamer 3, 4X fan 2, Combo player 3, Bluffer 3, Casual player 2 | [2026-10-21-ai-tastes-2/](2026-10-21-ai-tastes-2/) |

First session: every taste gave the game 3 out of 5: "quick, clean, readable, with real tension in the secret simultaneous orders",
but each one found it missing the thing its favourite games are about.

## Second session: after upgrades and the strategy retune

The average rating fell from 3.0 to 2.5. The reviewers were asked one new question: did building and fighting feel
worth it? They didn't know what had changed.

**Fighting now pays, too much.**
- In match 1 the Wargamer summoned 4 Golems in round 1, made six attacks and took a tile with every one. It won 46
  to 21 to 14 and still ended with 61 mana unspent.
- Nearly every attack hit an empty tile. "It was a march rather than a war" (Wargamer). "The most efficient action
  was to take someone else's engine" (Euro).
- Every taste that fought said each capture (2 points, plus the tile and its upgrade) was its best source of points.

**Attune at 1 + 3 per tile closed peaceful growth (4 of 6: Euro, 4X, Combo, Casual).**
- "Attune cost 10, then 13, so I could only grow once" (Casual).
- "Seat 1 never attuned once. Free tiles sat unclaimed all game" (4X).
- "Growth came only from fighting" (Combo).
- The bot measurement picked 1 + 3 because it evened out the strategy bots. But the bots attune only in round 1 too,
  so the measurement couldn't show what the price does to a player who wants to grow.

**Blight on the same tile, round after round (victims in 3 of 3 matches).**
- The 4X fan's Shrine tile, the Euro-gamer's Wild tile and the Casual player's best tile were each blighted in three
  rounds. "I had no way to stop it" (Casual).
- In match 3 the Blights also cost the Casual player's Golems their upkeep.
- The Bluffer, who cast them, called it "a win from hexing rather than fighting".

**No catch-up; the leader runs away (Wargamer, 4X, Bluffer, Casual).**
- Losers fall further behind: less land, less income, Blighted, Scorched.
- In match 3 a passive bot won by sitting on 64 mana and building three upgrades, while the two AI seats fought
  each other. "Too often a player who ignores everyone still wins" (Bluffer).

**Upgrades: worth their mana as points, not as an engine.**
- Every taste called them "a safe 1 point" for 5 mana. Most were bought late, to spend spare mana.
- The Combo player's round-1 Sanctum powered its whole Hexer plan, which it liked best.
- The asks of last session (something that compounds, a long arc from small to mighty) are only half met. Upgrades
  don't combine, and they pass to whoever takes the tile.

**Also:**
- Scorch can burn a whole army before it attacks ("has no units left to send"). The caster loved it; the victims
  didn't (4X, Casual).
- Trait goals fill early. Hexer was full by round 3 or 5 (Combo, Bluffer).
- Golem upkeep punishes Golemwright (Casual).
- Summoning and attacking in one round was asked for again (Wargamer, Bluffer).

**Wishes, new this session:**
- a gentler or flat Attune (4X, Euro, Casual);
- a limit on Blighting the same tile or player again, or a way to cleanse it (4X, Casual);
- a catch-up for the last player (Wargamer, Casual, 4X);
- upgrades that combine with each other (Combo, Euro);
- a reward for the loser of a battle (Wargamer);
- traits kept secret until the end (Bluffer);
- non-binding pacts (Bluffer).

## First session: what they liked (most tastes)

- **Secret, simultaneous orders.** Everyone liked guessing which tile a rival would hit, and every taste had a
  "gotcha" moment. The Bluffer, Casual player and Wargamer called it the best part.
- **`check` and the ledger.** Players always knew what an order would cost and do (Euro, 4X, Casual).
- **Banked points** that can't be taken away, and the Attune price curve: every expansion is a real sum (Euro).
- **Small combos:** Lure, then a lone Golem that a loss of 2 can't kill (4X, Combo); summon, then Leyline (Euro);
  Scorch, then attack (Combo, Wargamer).
- **Unveils as exploration** (4X, Casual).

## First session: what they missed

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

Target tastes (the game is for them, `../design-principles.md`): Wargamer, 4X fan, Bluffer, Casual player. Outside
tastes: Euro-gamer, Combo player. "s1" and "s2" link to a review in the first and second session.

| Addition | Asked for by | Size |
|---|---|---|
| ~~Tile upgrades (a shrine or tower: yield, defence or cheaper spells)~~ | 4X fan ([s1](2026-10-21-ai-tastes/match2-seat1.md)); outside: Euro-gamer ([s1](2026-10-21-ai-tastes/match1-seat1.md)), Combo player ([s1](2026-10-21-ai-tastes/match2-seat2.md)) | done in #77 |
| A gentler Attune curve, so a small realm can grow without fighting (done in #81) | 4X fan ([s2](2026-10-21-ai-tastes-2/match2-seat1.md)), Casual player ([s2](2026-10-21-ai-tastes-2/match3-seat2.md)); outside: Euro-gamer ([s2](2026-10-21-ai-tastes-2/match1-seat1.md)), Combo player ([s2](2026-10-21-ai-tastes-2/match2-seat2.md)) | small: one number or formula |
| A Blight limit (not the same tile two rounds running) or a way to cleanse it (done in #81) | 4X fan ([s2](2026-10-21-ai-tastes-2/match2-seat1.md)), Casual player ([s2](2026-10-21-ai-tastes-2/match3-seat2.md)); outside: Euro-gamer ([s2](2026-10-21-ai-tastes-2/match1-seat1.md)) | small: one rule |
| Catch-up for the last player | Wargamer ([s2](2026-10-21-ai-tastes-2/match1-seat2.md)), 4X fan ([s2](2026-10-21-ai-tastes-2/match2-seat1.md)), Casual player ([s2](2026-10-21-ai-tastes-2/match3-seat2.md)) | small to medium |
| Upgrades that combine (a Shrine next to a Sanctum, Towers side by side) | outside: Combo player ([s2](2026-10-21-ai-tastes-2/match2-seat2.md)), Euro-gamer ([s2](2026-10-21-ai-tastes-2/match1-seat1.md)) | medium |
| Public objectives or region goals (shared targets that pay a chunk) | 4X fan, Casual player, Wargamer ([s1](2026-10-21-ai-tastes/match2-seat1.md), [s1](2026-10-21-ai-tastes/match3-seat2.md), [s1](2026-10-21-ai-tastes/match1-seat2.md)) | medium: a small deck of goals, drawn from the seed |
| Summon limits near enemies (armies placed before the fight) | Wargamer ([s1](2026-10-21-ai-tastes/match1-seat2.md)), Bluffer ([s1](2026-10-21-ai-tastes/match3-seat1.md)), and the earlier personality sessions | small: one rule |
| Pacts and messages (non-binding) | Bluffer ([s1](2026-10-21-ai-tastes/match3-seat1.md), [s2](2026-10-21-ai-tastes-2/match3-seat1.md)), Casual player ([s1](2026-10-21-ai-tastes/match3-seat2.md)) | larger: a page feature, matters only with people |
| Hidden combat commitments (face-down reinforcements) | Bluffer ([s1](2026-10-21-ai-tastes/match3-seat1.md)), Wargamer ([s2](2026-10-21-ai-tastes-2/match1-seat2.md)); outside: Combo player ([s1](2026-10-21-ai-tastes/match2-seat2.md)) | medium |
| A research track | 4X fan ([s1](2026-10-21-ai-tastes/match2-seat1.md), [s2](2026-10-21-ai-tastes-2/match2-seat1.md)); outside: Combo player ([s1](2026-10-21-ai-tastes/match2-seat2.md)) | larger |
