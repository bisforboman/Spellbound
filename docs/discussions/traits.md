# Traits: the pool (decided 20 and 21 Oct 2026)

How the traits were chosen. The owner first took the six below, with each match offering the same 3 to everyone. On
21 Oct 2026 they asked for every player's trait to be unique: **each player is offered their own 2, none shared**, so
the pool grew to 10 with four traits about spells and units (*Four more*, below). The rules and numbers as built are in
[game-design.md](../game-design.md#traits); the numbers in the first table below were the first guesses.

## What the design already asks of a trait

- **Everyone is offered the same 3 and drafts 1 in secret, hidden through the opening.** The opening auctions are the
  only time the secret matters, so every trait needs an **opening tell**: it must make a player want different tiles
  than the other traits do. Then rivals can guess at traits from how they bid, which is the point of hiding them.
- **Build around it, not a small bonus.** Each trait should change which stance a player reaches for (Attune to
  expand, Summon then Attack to fight, or clearing monsters), not just add points.
- **Trait goals are hidden until the end, and the main score is visible.** So every goal is **capped**, or the
  visible scoreboard would mean little. The caps below were 8–10 points: about 15% of a typical final score (the
  simulator's 3-player bot games then ended around 55 points). *Since 20 Oct 2026 scores are about a third of that
  (around 18), so the caps are now 3 and the goals a third as rich; the table in game-design.md has the numbers.*
- **The biggest realm pays the most to grow** (principle 13) and **everyone stays in the game** (principle 12). So no
  trait makes Attune cheaper or adds mana: those snowball. Every trait here is an **end-of-game score** over things the
  game already tracks, so it is small to build and easy to balance.
- **Two players may draft the same trait.** That only makes them fight over the same tiles, which is fine.

## The pool (6 traits; each match offers 3)

| Trait | Pulls towards | Opening tell | Goal at the end (hidden), cap |
|---|---|---|---|
| **Wildheart** | Wild land, the gamble | Bids on Wild tiles, even a Wild 0 nobody else wants | 2 points per Wild tile held, up to 10 |
| **Weaver** | One solid realm | Bids on tiles next to its own | 1 point per tile in its largest connected group, up to 10 |
| **Wanderer** | Many footholds far apart | Bids on tiles away from its own | 2 points per separate group of tiles, up to 8 |
| **Beastbane** | Hunting monsters | Bids on tiles next to plenty of dark land (where the guarded tiles will appear) | 2 points per monster tile cleared, up to 8 (on top of the conquest point) |
| **Warbrand** | Taking land from rivals | From the second Unveil on, bids on tiles next to rivals' | 2 points per rival tile taken, up to 10 (on top of the conquest point) |
| **Hearthkeeper** | Holding what you have | Bids on Steady tiles, the safe ones | 2 points per Steady tile held, up to 10 |

Notes, trait by trait:

- **Wildheart** is the design's own example ("a trait that scores for land of one type"). A Wild 0 tile yields
  nothing but still scores for it, so its bids can look strange to rivals: a good tell. *Risk:* Wild is a third of the
  map, so it may find its land too easily; the cap holds it.
- **Weaver** and **Wanderer** are opposites with the clearest tells, and they disagree about Attune: the Weaver attunes
  to join its groups, the Wanderer attunes in different directions and avoids merging them. *Risk:* Wanderer's groups
  are easier to attack piece by piece; that is its cost. A player's last tile can't be taken, so it never ends up with
  nothing.
- **Beastbane** has the weakest opening tell (the opening never reveals guarded tiles), so offer it alongside two
  traits with clear tells. It makes Golems worth it: one Golem ties a guard of power 3 and wins the tile.
- **Warbrand** pushes the game towards attacks, which the paper prototype asks about ("is anyone ever attacked?").
  *Risk:* a lead from attacking also feeds the visible score, so it could run away; the simulator will show it.
- **Hearthkeeper** is Wildheart's mirror on Steady land. It makes the most sense offered together with Wildheart, so
  the two compete in the auctions for different tiles.

Left out on purpose: a "hoard mana" trait (it rewards doing nothing, the opposite of what the prototype worries
about); anything that changes the Attune price or income (principles 12 and 13); new stances or units (the roadmap
puts those later).

## How the offer works

- Each match offers **3 of the 6, chosen from its seed**, so the guessing changes from match to match. (The design's
  "the same 3" means the same 3 for everyone in a match; the roadmap's "Traits (full list)" suggests a pool.)
- A set of 3 should pull towards different tiles and stances. A simple rule: never offer both Wildheart and
  Hearthkeeper without a third trait that isn't about a tile type.
- At the end, each trait goal is revealed in the report ("Wildheart: 4 Wild tiles, 8 points") before the winner.

## How we'd check the numbers

Once chosen, each trait is small to build: a seat holds its trait, the end of the match adds its goal, and the bots'
bid rule leans towards the trait's tiles. Then the simulator reports **win rate and goal points by trait** over
thousands of seeded games, before any person plays them. A trait that wins too often gets a lower cap.

## Four more (21 Oct 2026)

Each player now gets their own offer of 2, so 5 players need 10 traits. The owner chose to let the new ones build on
spells and units, which didn't exist when the first six were chosen. They are still end-of-game, capped goals:

| Trait | Pulls towards | Opening tell (intended; bots don't bid this way yet) | Goal at the end, cap 3 |
|---|---|---|---|
| **Golemwright** | Golems | Bids on high yields, to pay their upkeep | 1 point per 2 Golems you have |
| **Spellweaver** | Every kind of spell | Bids on tiles next to rivals and monsters, where most spells aim | 1 point per different spell beyond the first |
| **Warden** | Keeping the land you started with | Bids on tiles it can defend: next to its own | 1 point per opening tile still held, beyond the first |
| **Hexer** | Scorch and Blight | Bids on tiles next to rivals | 1 point per 2 Scorches and Blights |

Tried and changed on the way, in the simulator:

- **Warden** first scored attacks beaten back. Bots only attack when they'd win, so it scored almost nothing (0.1 a
  match); "tiles with units on them" scored nothing either (bots fight from one stack). Keeping the opening's tiles
  works, but counting all three made it the strongest trait (42% of 3-player wins), hence "beyond the first".
- **Wildheart and Hearthkeeper** went from 1 point per 2 tiles of their type to 1 per tile: at 1 per 2 they averaged
  under 1 point and won least.
- **Hexer** stays at 1 per 2: at 1 per 3 it won least.

### The bots changed too

At first only the bots with a unit trait summoned before attuning (every other round), and they won 46% of 3-player
games whatever their goal scored: the simulator was measuring that plan, not the traits. Now **every bot** summons
before attuning every other round, and traits only change what and where (tile type, Golems, where to summon, which
spell). This plays better than attuning almost every round: conquest points per seat went from 0.8 to 2.5, mana left
at the end from 26 to 13, and summons from 4 Golems and 12 Imps a game to 12 and 30. Scores stay around 18.
