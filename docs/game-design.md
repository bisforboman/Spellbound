# Spellbound — game design

The rules of the game, built step by step from a clean slate. Why they look this way is in `design-principles.md`. Anything not written here is still open; earlier versions of the rules are not assumed. All numbers are first guesses until tested.

Decided discussions (background and the angles that were considered):
- `discussions/player-elimination.md`: a player's last tile can't fall.
- `discussions/scoring-and-end.md`: 8 rounds, points banked as you go.

## How a game plays out

A walkthrough of one game from start to finish, kept up to date as rules are settled.

1. **Setup.** The map lies dark. Each player starts with **9 mana** and nothing else. Everyone is offered the **same 3 traits** and drafts 1, in secret. There are no fixed starting positions: players start wherever the opening auctions put them.
2. **The opening Unveils.** Traits are still hidden, so nobody knows for sure why a rival bids hard on a tile. Before the first round, three Unveils reveal **3 tiles per player plus 3 extra**, one tile at a time. In each one, as many tiles as there are players go up for a secret mana auction: winners take the tile they bid on, and everyone else gets a random tile nobody wanted. The rest of the revealed tiles stay unowned, for later. By the end of the opening, every player owns 3 scattered or connected tiles and can see where the good land lies.
3. **The rounds.** The game lasts **8 rounds**. When round 1 starts, all traits are revealed. Each round, every player secretly picks one **stance** (Attack, Attune or Summon) and may move units freely; then everything resolves together. Nobody starts with units, so the first rounds are about summoning and attuning.
4. **Expanding.** Players grow into unowned land next to theirs: by **attuning** free tiles with mana, or by attacking the monsters guarding some of them. Attuning is also how scattered holdings get joined up, so units can move between them.
5. **The middle Unveils.** In **rounds 2 and 4**, the tiles still dark after the opening are revealed, split evenly between the two Unveils (no auction). About 1 in 3 of these tiles is guarded by monsters. After round 4, the whole map is revealed.
6. **The second half.** Rounds 5–8: with the map fully revealed and free land running out, growth turns into competition. Attuning gets more expensive as realms grow, so attacking rivals becomes the cheaper way to gain land. A player's last tile can never be taken.
7. **The end.** After round 8, the player with the most points wins. Points are banked every round, so what you earned early can't be taken away. Trait-goal points are hidden until the end.

## Traits

- At setup, **every player is offered the same 3 traits** and drafts **1**. Two players can end up with the same trait.
- A trait is something to **build around**: it should pull a player towards a strategy (certain tiles, a way of expanding, a style of play), not just add a small bonus.
- Traits are **hidden during the opening Unveils** and **revealed when the regular rounds start**. During the opening auctions, players can only guess at each other's traits from how they bid.
- Traits can give **trait goals**: their own way to score (see *Scoring and winning*).
- **The pool** (decided 20 Oct 2026; the reasoning is in `discussions/traits.md`). Each match offers **3 of these 6**,
  chosen from its seed. Every trait goal is scored once, at the end, and is capped:

| Trait | Pulls towards | Goal at the end (hidden), cap |
|---|---|---|
| Wildheart | Wild land | 1 point per 2 Wild tiles held, up to 3 |
| Hearthkeeper | Steady land | 1 point per 2 Steady tiles held, up to 3 |
| Weaver | one connected realm | 1 point per 3 tiles in your largest connected group, up to 3 |
| Wanderer | many separate footholds | 1 point per separate group of tiles beyond the first, up to 3 |
| Beastbane | clearing monsters | 1 point per monster tile cleared, up to 3 (on top of the conquest point) |
| Warbrand | taking rivals' land | 1 point per rival tile taken, up to 3 (on top of the conquest point) |

- No trait changes the Attune price, income or upkeep: they would snowball (principles 12 and 13).
- *Open:* how often each trait wins; the simulator will report win rate and goal points by trait once they're built.

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
  - **Cast:** spend mana on one spell (see *Spells*).
- Alongside the stance, players can **move units** freely (see *Units*).
- A stance can be committed **aimed at nothing** (no tile to attune, nothing to summon): only the moves happen. A player is never forced to spend.
- More stances (for example a defensive Hold, or Scry to peek at hidden things) can be added after the first playtest.

### Round order

In rounds 2 and 4, the round starts with a **later Unveil**. Then:

1. **Income:** every player collects the mana yield of each tile they hold.
2. **Upkeep:** pay 1 mana per Golem; a Golem that can't be paid for dissolves.
3. **Commit:** every player secretly commits one stance plus any moves. When all have committed, everything is revealed.
4. **Moves** resolve. Defenders can reinforce a threatened tile in the same round; attackers can't be sure what they'll meet.
5. **Attune, Summon and Cast** resolve. New tiles exist, new units can defend, and spells take effect before attacks
   (Blight waits for the next round's income).
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
- **Spells** use mana with the Cast stance (see *Spells*).

## Spells

Decided 20 Oct 2026 (the reasoning is in `discussions/spells.md`). **Cast** is a stance: a round you cast is a round
you don't attune, summon or attack. A spell's target is one of your tiles or a tile next to your land, its cost and
effect are shown before you commit, and its effect lasts **this round only** (except Lure's, below); no spell changes
yields, prices or income. Costs are flat and small.

| Spell | Cost | Target | Effect (this round) |
|---|---|---|---|
| Scorch | 2 mana per power | a rival's tile next to yours | Burns that much of its units' power, weakest first (the battle loss rule). Monsters are immune. |
| Ward | 3 mana | one of your tiles | Its defence counts 3 more power in this round's battles. |
| Leyline | 1 mana per unit (at least 2) | two of your tiles, any distance apart | Moves units between them, even across a gap; they count as moved. |
| Blight | 4 mana | a rival's tile next to yours | It yields nothing in the next round's income. |
| Lure | 3 mana | a monster tile next to yours | Its guards lose 1 power **for good**, never below 1 (decided 20 Oct 2026: as a stance, a this-round Lure could never be followed by your own attack). |

- **How spells resolve:** in seat order, after Attunes and Summons and before attacks. Two Wards on one tile add up, and
  two Scorches both burn.
- **Ward takes the blows first:** when a warded tile holds, its defenders lose only the attack power the Ward didn't
  absorb.
- **An attack goes with what's left:** if a rival's Scorch burns units on the tile an attack leaves from, the attack
  sends only the units still there, or doesn't happen if none are.
- **Leyline** can't carry units that also step this round.
- **Blight follows the tile:** if a blighted tile changes hands in the same round, its new owner gets nothing from it.

- *Open:* whether a Cast stance is enough of a use for mana. The first measurement (1,000 bot games, 3 players):
  bots that cast only with a purpose spend about 3 mana a match on spells and still end with **52 unspent**; bots that
  cast whenever they are rich spend 21 and end with 25, but attune less and attack less. So as a stance, spells use the
  surplus only by replacing Attunes. Casting alongside the stance, like moves, remains the alternative.

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
| Trait goals | set by each trait |

- **Tie-break:** most tiles held at the end; if still tied, most mana in hand.
- **Scale** (changed 20 Oct 2026): a 3-player game ends around **18 points** a seat (about 12 for land, 5 for mana,
  1 for conquest) instead of 55. Land and mana keep their balance; the winner is the same as before in about 94 of 100
  simulated games. A conquest point now weighs more (about 5% of a score instead of 2%), on purpose: it rewards attacks.

## Player elimination

- A player's **last tile can't be taken**: a winning attack on it kills the defenders but leaves the tile with its owner (see *Battles*). Every player always keeps a foothold to rebuild from.
- Together with banked points, this keeps everyone in the game to the end (principle 12).

## Next to shape

1. Building the traits and checking them in the simulator (win rate and goal points by trait).
2. A first paper playtest (`skirmish-paper-prototype.md`); then revisit stances and the numbers.
