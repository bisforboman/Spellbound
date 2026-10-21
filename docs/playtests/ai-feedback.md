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
| 21 Oct 2026 | after #85 (Attune 1 + 1 per tile to 3 then + 3; Blight limit; Rally; secret traits with guessing; fog of war) | **Target:** Wargamer 4, Bluffer 3, 4X fan 3, Casual player 2. **Outside:** Euro-gamer 3, Combo player 3 | [2026-10-21-ai-tastes-3/](2026-10-21-ai-tastes-3/) |
| 21 Oct 2026 | after #87 (an undefended capture scores 1; the Table lists the traits) | **Target only:** Wargamer 4, Bluffer 3, 4X fan 3, Casual player 3 | [2026-10-21-ai-tastes-4/](2026-10-21-ai-tastes-4/) |

First session: every taste gave the game 3 out of 5: "quick, clean, readable, with real tension in the secret simultaneous orders",
but each one found it missing the thing its favourite games are about.

## Fourth session: the raid rule, and trait guessing with the names listed

Only the four target tastes played (two matches). They average **3.25**: 3.0, then 2.5, then 3.0, now 3.25. The
Casual player rose from 2 to 3.

**Defended fights happen now.**
- The Wargamer: round 2 "was a real battle", 11 against 12, "painful and great, like guessing wrong in a Kemet
  combat" ([review](2026-10-21-ai-tastes-4/match1-seat1.md)).
- The Casual player's best moments were defending on a hunch: "power 11 against 12" and "8 against 15", "Catan-
  robber-dodging satisfying" ([review](2026-10-21-ai-tastes-4/match1-seat2.md)).
- The Bluffer: a 3-against-3 tie when a rival leylined in exactly enough, "being read that well hurt, and it was
  great" ([review](2026-10-21-ai-tastes-4/match2-seat1.md)).
- Raids on empty tiles still happen, mostly late (Wargamer, Casual player), but no taste called them the whole
  game.

**Trait guessing works as deduction, but rarely pays.**
- The Bluffer liked it: "every build and spell is a clue", and "I had considered both right answers and then talked
  myself out of them. That is the kind of mistake I like to make".
- The others found it "a coin flip worth one point" (Casual player).
- The AI players got 1 of 8 guesses right. In match 2 the bot got both of its guesses right.

**Mana piles up: one stance a round is the ceiling** (all four target tastes).
- The Wargamer: "I can never summon and attack in the same turn … 17-20 mana I could not spend".
- The Casual player: 19 unspent; a bot 38.
- The 4X fan: "22 to 38 unspent mana in several rounds with nothing to put it into".
- Growth also stops early: Attune at 10–13 mana from about round 4 (4X fan, Bluffer).

**A big hidden Scorch feels like being deleted.** A 27-mana Scorch burned all 3 of the Bluffer's Golems before they
attacked: "with enemy mana hidden there was no way to see it coming. It felt like being deleted rather than
outguessed" ([review](2026-10-21-ai-tastes-4/match2-seat1.md)). The 4X fan, who cast it, loved it.

**Also:**
- the coordinate map is homework (Casual player; the AI tool's text view);
- talk and pacts (Bluffer, a fourth time);
- the map feels small (4X fan, Wargamer).

## Third session: after the reading-rivals elements

This is the first session weighted by the owner's identity (`../design-principles.md`). The target tastes played matches 1
and 2; the outside tastes played match 3. The target tastes average 3.0 over the three sessions: 3.0, then 2.5, now 3.0. Trait guessing is judged
in the next session (see the defect below).
The new question was "did reading your rivals matter?"

**A defect in the AI tool, not the game:** the Table's rules never listed the trait names, so every guess was blind
([Bluffer](2026-10-21-ai-tastes-3/match1-seat2.md), [4X fan](2026-10-21-ai-tastes-3/match2-seat1.md),
[Casual player](2026-10-21-ai-tastes-3/match2-seat2.md), and both outside tastes). An unknown name crashed the tool.
The web form lists them. Fixed with this session's write-up: the Table's `rules` now lists all ten traits with their
goals. **So this session can't judge trait guessing.**

**Reading rivals: it works in battle, for the target tastes.**
- The Bluffer: "Yes, more than I expected, but only through military moves". Round 6 was "a proper read", and its
  rival's warded empty tile "showed me exactly what it had been afraid of"
  ([review](2026-10-21-ai-tastes-3/match1-seat2.md)).
- The Wargamer: "Outguessing mostly meant guessing which tile a rival had left empty, and that decided a lot of
  points. It never decided a big battle" ([review](2026-10-21-ai-tastes-3/match1-seat1.md)).
- The Casual player: "Yes, too much". It guessed the target right and still lost to "a hidden +2" Rally, then to a
  surprise Ward. "Outguessing never decided anything for me. Out-spending did"
  ([review](2026-10-21-ai-tastes-3/match2-seat2.md)).
- The 4X fan: "Barely": an empty tile next door is the only read needed
  ([review](2026-10-21-ai-tastes-3/match2-seat1.md)).

**Raids on empty tiles decide the game** (all four target tastes, and both outside ones).
- "Nearly every attack hit an empty tile … three armies passing each other" (Wargamer).
- "Whack-a-mole where I always lose" (Casual player).
- "By round 5 the right move was always the same: find an empty enemy tile … walk in, take 2 points" (Combo
  player, outside).
- Units that attack leave home undefended, a capture banks 2 points, and a defender can summon on only one tile. So
  defending loses to raiding.
- Rally on every attack "felt like a tax, not a choice" (Wargamer).

**One stance a round caps a growing realm** (4X fan; and outside, Euro-gamer and Combo player).
- The 4X fan: "37 mana and could still only do one thing … 30 mana unspent".
- The Combo player (outside): "42 mana unspent … my turn stayed the same size".
- The Wargamer, too, wants "a small summon alongside an attack".

**Building still loses to raiding.** Upgrades pass with the tile, so "a capture … takes the tile's upgrade with it"
(Euro-gamer, outside). The 4X fan and the Bluffer built nothing.

**To watch:** Rally plus a lone Golem clears a power-3 monster with no losses. Three reviewers found it on their own
(Combo player, Euro-gamer, Bluffer). The bot's defence is predictable: it "always guards the tile you are standing
next to" (Bluffer). That is a bot artefact, kept as a follow-up.

**Liked:** the Wargamer's tile won, lost and won back ("the best moment"); the Bluffer's reads; the Lure-then-Rally
and Leyline-then-attack combos (Combo player, 4X fan, Euro-gamer); `check`; banked points.

**Wishes, new this session (target tastes first):**
- a point or glory for losing a battle, so defended clashes pay (Wargamer, as in session 2);
- summon a little alongside an attack (Wargamer; also the Bluffer in session 1);
- a contested central tile or region worth points every round, to pull the stacks together (Wargamer);
- more actions as the realm grows, such as a second stance at 6+ tiles (4X fan; outside: Euro-gamer, Combo player);
- a catch-up for the last player (Casual player, as in session 2);
- a drawn map (Casual player; that is the AI tool's text view, and the game's page draws one);
- talk and pacts, and a decoy order (Bluffer).

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
| Defended fights that pay: points for losing a battle, or a capture of an empty tile worth less (third session) | Wargamer ([s3](2026-10-21-ai-tastes-3/match1-seat1.md), [s2](2026-10-21-ai-tastes-2/match1-seat2.md)), Casual player ([s3](2026-10-21-ai-tastes-3/match2-seat2.md)) | small: one rule |
| More actions as a realm grows, such as a second stance at 6+ tiles (third session) | 4X fan ([s3](2026-10-21-ai-tastes-3/match2-seat1.md)); outside: Euro-gamer ([s3](2026-10-21-ai-tastes-3/match3-seat1.md)), Combo player ([s3](2026-10-21-ai-tastes-3/match3-seat2.md)) | medium |
| A contested centre worth points every round (third session) | Wargamer ([s3](2026-10-21-ai-tastes-3/match1-seat1.md)) | small to medium |
| A research track | 4X fan ([s1](2026-10-21-ai-tastes/match2-seat1.md), [s2](2026-10-21-ai-tastes-2/match2-seat1.md)); outside: Combo player ([s1](2026-10-21-ai-tastes/match2-seat2.md)) | larger |
