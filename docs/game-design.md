# Spellbound — game design

The rules of the game, built step by step from a clean slate. Why they look this way is in `design-principles.md`. Anything not written here is still open; earlier versions of the rules are not assumed. All numbers are first guesses until tested.

Decided discussions (background and the angles that were considered):
- `discussions/player-elimination.md`: a player's last tile can't fall.
- `discussions/scoring-and-end.md`: 8 rounds, points banked as you go.

## How a game plays out

A walkthrough of one game from start to finish, kept up to date as rules are settled.

1. **Setup.** The map lies dark. Each player starts with **9 mana** and nothing else. Each player is offered **their own 2 traits** (nobody else is offered them) and drafts 1, in secret. There are no fixed starting positions: players start wherever the opening auctions put them.
2. **The opening Unveils.** Traits are still hidden, so nobody knows for sure why a rival bids hard on a tile. Before the first round, three Unveils reveal **3 tiles per player plus 3 extra**, one tile at a time. In each one, as many tiles as there are players go up for a secret mana auction: winners take the tile they bid on, and everyone else gets a random tile nobody wanted. The rest of the revealed tiles stay unowned, for later. By the end of the opening, every player owns 3 scattered or connected tiles and can see where the good land lies.
3. **The rounds.** The game lasts **8 rounds**. When round 1 starts, all traits are revealed. Each round, every player secretly picks one **stance** (Attack, Attune or Summon) and may move units freely; then everything resolves together. Nobody starts with units, so the first rounds are about summoning and attuning.
4. **Expanding.** Players grow into unowned land next to theirs: by **attuning** free tiles with mana, or by attacking the monsters guarding some of them. Attuning is also how scattered holdings get joined up, so units can move between them.
5. **The middle Unveils.** In **rounds 2 and 4**, the tiles still dark after the opening are revealed, split evenly between the two Unveils (no auction). About 1 in 3 of these tiles is guarded by monsters. After round 4, the whole map is revealed.
6. **The second half.** Rounds 5–8: with the map fully revealed and free land running out, growth turns into competition. Attuning gets more expensive as realms grow, so attacking rivals becomes the cheaper way to gain land. A player's last tile can never be taken.
7. **The end.** After round 8, the player with the most points wins. Points are banked every round, so what you earned early can't be taken away. Trait-goal points are hidden until the end.

## Traits

- At setup, **each player is offered their own 2 traits**, drawn from the pool by the match's seed, and drafts **1**.
  No trait is offered to two players, so every player's trait is unique (owner's choice, 21 Oct 2026; it replaced
  "the same 3 for everyone"). The pool of 10 covers 5 players. A player who hasn't drafted when the opening ends gets
  the first trait of their offer.
- A trait is something to **build around**: it should pull a player towards a strategy (certain tiles, a way of expanding, a style of play), not just add a small bonus.
- Traits are **hidden during the opening Unveils** and **revealed when the regular rounds start**. During the opening auctions, players can only guess at each other's traits from how they bid.
- Traits give **trait goals**: their own way to score (see *Scoring and winning*). Every goal is scored once, at the
  end, hidden until then, and capped at **3 points**.
- **The pool** (the first six decided 20 Oct 2026, the last four 21 Oct 2026; the reasoning is in
  `discussions/traits.md`):

| Trait | Pulls towards | Goal at the end (hidden), cap 3 |
|---|---|---|
| Wildheart | Wild land | 1 point per Wild tile held |
| Hearthkeeper | Steady land | 1 point per Steady tile held |
| Weaver | one connected realm | 1 point per 2 tiles in your largest connected group beyond the first 2 (4 tiles = 1, 6 = 2, 8 = 3) |
| Wanderer | many separate footholds | 1 point per separate group of 2 or more tiles (a lone tile doesn't count) |
| Beastbane | clearing monsters | 1 point per monster tile cleared (on top of the conquest point) |
| Warbrand | taking rivals' land | 1 point per rival tile taken (on top of the conquest point) |
| Golemwright | Golems | 1 point per 3 Golems you have at the end |
| Spellweaver | casting every kind of spell | 1 point per 2 different spells cast, and 1 for casting spells in 6 rounds |
| Warden | keeping the land you started with | 1 point per tile from your opening still held, beyond the first (so at most 2) |
| Hexer | Scorch and Blight | 1 point per Scorch or Blight cast (a Scorch counts only if it burns something) |

- No trait changes the Attune price, income, upkeep or a cost: they would snowball (principles 12 and 13).
- **Measured** (1,000 bot games each; bots draft at random, bid like their trait and lean towards their trait's
  tiles, units and spells): every trait wins between 25% and 39% of 3-player games (33% is even) and between 16% and
  24% of 5-player games (20% is even). Spellweaver and Weaver win least. Goals average 0.5 (Beastbane) to 1.9 points.
  *(21 Oct 2026, after the AI players' sessions: Spellweaver no longer hits its cap by round 4 (1 per 2 spells plus 1
  for casting in 6 rounds); Wanderer counts groups of 2 or more instead of groups beyond the first; Weaver scores
  from 4 tiles (bots' groups are often 3, so its bot average fell from 0.9 to 0.7); see `playtests/`.)*

## Map

- An **open hex board**: a round board of hexes, the same shape for any number of players.
- **Size by player count:** **19 hexes** for 2–3 players, **37 hexes** for 4–5.
- The map starts **dark**. Tiles are fixed places on it; both a tile's **type** and its **position** matter.
- A player's tiles can be **scattered**, as in Catan. The tiles you own form one or more **connected groups**.
- Units can only move **within a connected group**. Joining two groups (by attuning or taking the tiles between them) merges them, so units can then move across.

## Tiles

- Tiles **mainly produce** mana; holding land also scores points (see *Scoring and winning*).
- Start with **3 tile types**; more can be added later. All three produce **mana**; they differ in how much it varies:

| Type | Mana | Average |
|---|---|---|
| Steady | 2 | 2 |
| Shifting | 1–3 | 2 |
| Wild | 0–4 | 2 |

- A tile's yield is decided **once, when it is unveiled**: it gets a fixed value within its type's range and keeps it for the whole game. The value is visible from then on, so players know exactly what they are bidding on.
- **The odds** are the paper prototype's die: Shifting yields 1, 2 or 3 with equal chance; Wild yields 0 or 4 one time in six each, 1 or 3 one time in six each, and 2 two times in six.
- **Mix:** roughly **equal thirds** of each type (19 hexes: 7 Steady, 6 Shifting, 6 Wild).

## Unveil

- An **Unveil** turns over a batch of tiles on the dark map, one at a time.
- **The opening:** before round 1, three Unveils reveal **3 tiles per player + 3 free tiles** in total (one free tile in each Unveil). Each has an auction (below), so every player ends with 3 tiles and 3 free tiles are left to attune.

| Players | Map | Opening reveals | Auctioned | Left free | Still dark |
|---|---|---|---|---|---|
| 2 | 19 | 9 | 6 | 3 | 10 |
| 3 | 19 | 12 | 9 | 3 | 7 |
| 4 | 37 | 15 | 12 | 3 | 22 |
| 5 | 37 | 18 | 15 | 3 | 19 |
- **The later Unveils:** in **rounds 2 and 4**. The tiles still dark after the opening are **split evenly** between them (an odd one goes to round 2). They have **no auction**: the tiles stay unowned until someone takes them. After round 4 the whole map is revealed.
- About **1 in 3** tiles revealed in a later Unveil is **guarded by monsters** (decided at random). The opening Unveils never reveal guarded tiles.

### The Unveil auction (opening only)

- In each opening Unveil, **as many tiles as there are players** are auctioned; the other unveiled tiles are only revealed.
- Each player secretly bids **mana** on one of the auctioned tiles, or passes.
- The highest bid on a tile wins it and pays. **Losing bidders don't pay.**
- Players who lost or passed each get **one of the tiles nobody bid on, at random, for free**. So everyone ends each Unveil with exactly one new tile.
- **Ties:** if the top bids on a tile are tied, nobody gets it and nobody pays; the tile becomes one of the random leftovers.
- The smallest bid is **1 mana**; to spend nothing, pass. So bidding always costs something if you win, and passing keeps its meaning. Leftovers are handed out in seat order, each drawn at random from those still left.
- Won or leftover tiles don't need to connect to your other tiles (scattered holdings are allowed).

## Rounds and stances

- Each round, every player secretly picks **one stance**. Then all stances resolve together.
- A stance should be **one button** on a phone: the round's main decision is a single tap.
- **Four stances**:
  - **Attack:** attack a rival's or a monster-guarded tile with units (see *Battles*).
  - **Attune:** spend mana to claim one free tile (see *Taking unowned tiles*).
  - **Summon:** spend mana to summon units (see *Units*).
  - **Build:** spend mana on an upgrade for one of your tiles (see *Upgrades*).
- Alongside the stance, players can **cast one spell** (see *Spells*).
- Alongside the stance, players can **move units** freely (see *Units*).
- A stance can be committed **aimed at nothing** (no tile to attune, nothing to summon): only the moves happen. A player is never forced to spend.
- More stances (for example a defensive Hold, or Scry to peek at hidden things) can be added after the first playtest.

### Round order

In rounds 2 and 4, the round starts with a **later Unveil**. Then:

1. **Income:** every player collects the mana yield of each tile they hold.
2. **Upkeep:** pay 1 mana per Golem; a Golem that can't be paid for dissolves.
3. **Commit:** every player secretly commits one stance plus any moves. When all have committed, everything is revealed.
4. **Moves** resolve. Defenders can reinforce a threatened tile in the same round; attackers can't be sure what they'll meet.
5. **Attune, Summon and Build** resolve, then **spells**. New tiles exist, new units can defend, and spells take effect before
   attacks (Blight waits for the next round's income).
6. **Attacks** resolve.
   - Units sent to attack **have left** their tile: they don't defend it if it's attacked in the same round.
   - If **several players attack the same tile**, all of them fight the defender together in **one battle**; if the attackers win, the surviving attackers then fight each other for the tile.
   - Units that moved this round can attack: moves resolve first.
   - Each attacked tile has one battle, and the battles are fought in board order (this decides whether a tile is still its owner's last when its battle comes).
7. **Scoring:** points are banked (see *Scoring and winning*).

## Units

- All units are **summoned** with the **Summon** stance, and summoning costs **mana**.
- Units can be summoned on **any tile you own**. The player **taps the tile** where the units appear, then **confirms**.
- A tile can hold **any number of units** (no cap).
- One summon calls **one kind of unit**. The player chooses how much mana to spend, and that decides how many arrive (the rules take the number of units; the page shows what the mana buys).
- Two kinds of unit to start with:

| Unit | Battle power | Summon cost | Upkeep |
|---|---|---|---|
| Imp (basic) | 1 | 1 mana | none |
| Golem (strong) | 3 | 2 mana | 1 mana per round |

- Because weak units die first in battle and shield strong ones, strong units must cost more per point of power over time; the upkeep is what does that.
- Upkeep is paid **after** the round's mana income. If a player still **can't pay upkeep** for a Golem, that Golem **dissolves**. When only some can be paid for, the unpaid ones dissolve from the player's tiles in board order (a fixed order, so the result is always the same).
- **Moving units is possible every round**, regardless of stance, with **no limit** on the number of moves (to be tuned if visits get long).
- Units can only move **between your own tiles**, one step to an **adjacent** tile. Reaching beyond your borders takes the **Attack** stance.
- **One step per round:** a unit that moved this round can't move again until the next, so units cross a group one tile per round.
- Imps can pile up without limit. This is accepted for now; if playtests show a cheap horde is too strong, add a limit then (per-tile cap, Imps fading after a few rounds, or weaker Imps).

## Taking unowned tiles

A revealed, unowned tile is taken in one of two ways, depending on whether it is guarded:

- **Unguarded tiles, by attuning:** the **Attune** stance spends mana to tune a free, unguarded tile to your mana, making it yours. The tile must be **adjacent to any tile you own**.
  - Attuning extends where your units can go: it is how you grow your presence and join scattered groups together.
  - One Attune takes **one tile**.
  - **Price: 1 mana + 1 mana for every tile you already control.** With 3 tiles, an Attune costs 4. The price is the same whichever tile you attune; it only grows with the size of your realm, so the biggest realm pays the most to grow (principle 13).
  - **Clash:** if two players attune the same tile in the same round, nobody gets it and nobody pays.
- **Guarded tiles:** some unowned tiles are **guarded by monsters**. They can't be attuned; you need units to defeat the guards first.
  - Guard strength is **visible**.
  - Guarded tiles are cleared with the **Attack** stance, the same way as a rival's tile.
  - Clearing gives **just the tile**; monsters are an obstacle, not a prize.
  - Every monster guard has **battle power 3** (fixed, for now). One Golem ties it, which still wins the tile (see *Battles*); four Imps clear it outright.

## Taking a rival's tiles

- A rival's tile can **only be taken by attacking** it with units (the Attack stance). It can never be bought with mana.
- The strength of a rival's units is **visible**, so an attack can be judged before it is committed.
- A player's **last tile can't be taken** (see *Player elimination*).
- Battles follow the rules in *Battles*.

## Battles

Kept simple for now, to be extended later.

- Every unit has a **battle power** (see *Units*). Each side adds up the battle power of its units in the fight.
- **The higher total wins.** No dice: anyone can work out the result from the numbers.
- **Losses:** both sides lose battle power equal to the **smaller total**. So the loser is wiped out, and the winner keeps the difference.
  - Example: 8 attacks 5. The defender loses all 5; the attacker loses 5 power and keeps 3.
- **Which units die:** the **weakest units first**, automatically.
- **Rounding down:** the winner removes units weakest-first until removing the next one would exceed the power lost. Because the winner's total is higher, **a winner always keeps at least one unit**. Strong units are shielded by weak ones (a Golem beating two Imps loses nothing).
- **Ties go to the defender:** both armies are wiped out, and the tile stays with the defender (now empty).
- **Exception for monsters:** in a tie against monster guards, both sides are wiped out, but the **attacker takes the tile**. To be checked in playtests.
- **A won attack:** the attacker's surviving units **move into** the captured tile.
- Battles against monsters use the same rules, with the guards as the defender.
- **No defender bonus** for now: ties already go to the defender, so an attacker needs strictly more power. A bonus (from a stance, terrain or units that stay put) can be added later.
- **A rival's last tile:** if an attack on it wins, the defending units die, but the tile **stays with the defender** (empty). The attacker's units stay where they were, and **no conquest point** is scored. The surviving attackers go back to the tile they attacked from (the first in board order, if they came from several); if that tile was lost in the same round, they dissolve.
- **Several attackers** (decided 19 Oct 2026):
  - Together they lose power to the defender **weakest units first, across all of them**, seat order deciding between equal units.
  - If they win, the **strongest survivor takes the tile** and loses power equal to the next strongest's total (weakest first, as always); the other survivors are wiped out.
  - A **tie at the top** wipes out all the survivors and nobody takes the tile: it stays with the defender (empty), or for a monster tile it stays free, its guards gone.
  - A tie against monsters gives the tile to the attacker who sent the most power; if that's a tie too, nobody gets it.
- **Monster guards** fight as one unit of their power, so a weaker attack never wears them down.

## Resources

- **Mana:** produced by tiles (see *Tiles*); pays for opening bids, attuning and summoning.
- **Starting mana: 9.** Players start with nothing else: no tiles until the opening, and no units.
- **Unspent mana carries over** to the next round, including whatever is left after the opening auctions.
- **Spells** use mana alongside the stance (see *Spells*).

## Spells

Decided 20 Oct 2026 (the reasoning is in `discussions/spells.md`). **A spell is cast alongside the stance**, like
moves: optional, at most one a round, committed in secret with the stance; the stance and the spell are paid together.
*(First decided as a fourth stance, "Cast"; changed the same day once the simulator showed that, as a stance, spells
used the surplus mana only by replacing Attunes.)* A spell's target is one of your tiles or a tile next to your land, its cost and
effect are shown before you commit, and its effect lasts **this round only** (except Lure's, below); no spell changes
yields, prices or income. Costs are flat and small.

| Spell | Cost | Target | Effect (this round) |
|---|---|---|---|
| Scorch | 3 mana per power that burns | a rival's tile next to yours | Burns up to that much of its units' power, weakest first (the battle loss rule). You pay only for what burns. Monsters are immune. |
| Ward | 3 mana | one of your tiles, or the tile you attune this round | Its defence counts 3 more power in this round's battles. |
| Leyline | 1 mana per unit (at least 2) | two of your tiles, any distance apart | Moves units between them, even across a gap; they count as moved. |
| Blight | 3 mana | a rival's tile next to yours | It yields nothing in the next round's income. |
| Lure | 3 mana | a monster tile next to yours | Its guards lose 1 power **for good**, never below 1 (decided while spells were a stance, when a this-round Lure could never be followed by your own attack; it stays). |

- **How spells resolve:** in seat order, after Attunes and Summons and before attacks. Two Wards on one tile add up, and
  two Scorches both burn.
- **Ward takes the blows first:** when a warded tile holds, its defenders lose only the attack power the Ward didn't
  absorb.
- **An attack goes with what's left:** if a rival's Scorch burns units on the tile an attack leaves from, the attack
  sends only the units still there, or doesn't happen if none are.
- **Spells see the round as it will be** at their turn: a Ward can go on the tile you attune this round (it holds if
  the Attune does), and a Leyline can carry units you summon this round.
- **Leyline** can't carry units that also step this round; the units it carries can attack the same round.
- **Scorch before your own attack:** a Scorch and an attack on the same tile in one round work together: the Scorch
  burns first, and the attack's preview counts it (as it counts a Lure on a monster tile you attack).
- **Scorch pays for what burns** (decided 21 Oct 2026, after the first AI players' session,
  `playtests/2026-10-21-ai-players.md`): units that moved away before it, units another seat's Scorch burned first,
  or a Golem too strong to burn whole cost nothing. Its price went from 2 to 3 mana per power: at 2 it beat
  summoning defenders, because it stopped an attack before it landed without guessing which tile it would hit.
  A Scorch too small to burn anything there (1 power against a Golem) is refused; one that ends up burning nothing
  costs nothing and doesn't count as a cast for Hexer or Spellweaver (a free trait point otherwise, found by the
  second AI session).
- **Blight follows the tile:** if a blighted tile changes hands in the same round, its new owner gets nothing from it.

- **Measured** (1,000 bot games, 3 players): cast alongside the stance, bots spend about 20 mana a match on spells
  and end with about 26 unspent (52 when spells were a stance), while attuning as often as before. Bots reach for
  Blight most (about 12 a game), then Ward (about 3); Scorch almost never. *Since 21 Oct 2026 bots also summon every other round
  (see `discussions/traits.md`): they spend about 19 mana a match on spells and end with about 13 unspent, and Ward
  is cast most (about 9 a game), then Blight (about 5).*

## Upgrades

Decided 21 Oct 2026 (the reasoning is in `discussions/upgrades.md`): three of six AI tastes asked, separately, for
something built on a tile that keeps paying (`playtests/ai-feedback.md`).

- **Build is a stance:** one upgrade on one of your tiles, **5 mana**, at most **one per tile**, and you can't build
  while your tiles hold **3** upgrades (taken ones count).
- An upgrade belongs to the tile: it **passes with the tile** when the tile is taken, like a Blight.
- **Each upgrade you hold at the end is worth 1 point.**
- Builds resolve with Attunes and Summons, before spells and attacks.

| Upgrade | Effect, for whoever holds the tile |
|---|---|
| Shrine | The tile yields 1 more mana. |
| Tower | The tile defends with 2 more power, even with no units on it; like a Ward, it takes the blows first when the defence holds. |
| Sanctum | Its holder's spells aimed at it or a tile next to it cost 1 less. |

- The Shrine changes income and the Sanctum changes costs, which the rest of this document avoids because it snowballs;
  the cap of 3 per realm holds them in check (the owner's choice over a rising price).
- **Measured** (1,000 bot games each; bots build from round 3 when they would keep 6 mana beyond the price, a Tower on
  a threatened tile, a Sanctum for Hexer or Spellweaver, else a Shrine): bots spend about 10 mana a match on upgrades,
  final scores rise from about 19 to 19.5, and trait win rates spread a little (26–45% with 3 players, 13–27% with
  5); Beastbane wins most with bots. *To check with AI players next.*

## Scoring and winning

- The game lasts **8 rounds**. After round 8, the player with the **most points wins**.
- Points are **banked as you go**: earned during the game and never taken away, even if land is lost later.
- **What scores:**
  - **Land held:** points for the tiles you hold.
  - **Mana produced:** points for the mana your land produces.
  - **Conquest:** points for rival tiles taken and monster tiles cleared.
  - **Trait goals:** each trait can add its own way to score.
- **Visibility:** the main score is **visible** to everyone during the game; **trait-goal points are hidden** until the end.
- **Point values**, banked at the end of every round:

| Source | Points |
|---|---|
| Land held | 1 per 3 tiles held, counted every round and added up (holding 4 tiles for 3 rounds is 12, so 4 points; remainders carry over) |
| Mana produced | 1 per 12 mana your land produced (remainders carry over to the next round) |
| Conquest | 1 per rival tile taken or monster tile cleared (kept even if the tile is lost again) |
| Trait goals | set by each trait, scored once at the end, up to 3 (see *Traits*) |
| Upgrades | 1 per upgrade you hold at the end |
| Unspent mana | 1 per 10 mana left at the end, scored once (decided 21 Oct 2026: both AI sessions ended with 27–67 mana unspent, and late Scorches were dumps of mana worth nothing) |

- **Tie-break:** most tiles held at the end; if still tied, most mana in hand.
- **Scale** (changed 20 Oct 2026): a 3-player game ends around **18 points** a seat (about 12 for land, 5 for mana,
  1 for conquest) instead of 55. Land and mana keep their balance; the winner is the same as before in about 94 of 100
  simulated games. A conquest point now weighs more (about 5% of a score instead of 2%), on purpose: it rewards attacks.
  *Since 21 Oct 2026 bots summon and fight more (see `discussions/traits.md`): about 10 for land, 4 for mana and 2.5
  for conquest, and about 1 from trait goals: still about 18 in all.*

## Player elimination

- A player's **last tile can't be taken**: a winning attack on it kills the defenders but leaves the tile with its owner (see *Battles*). Every player always keeps a foothold to rebuild from.
- Together with banked points, this keeps everyone in the game to the end (principle 12).

## Next to shape

1. A first paper playtest (`skirmish-paper-prototype.md`); then revisit stances and the numbers.
