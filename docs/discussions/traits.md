# Traits: a first pool (proposal, 20 Oct 2026)

A proposal for the open question in [game-design.md](../game-design.md#traits), "the traits themselves". Nothing here is
a rule yet; the owner picks, and then the chosen ones go into the design document.

## What the design already asks of a trait

- **Everyone is offered the same 3 and drafts 1 in secret, hidden through the opening.** The opening auctions are the
  only time the secret matters, so every trait needs an **opening tell**: it must make a player want different tiles
  than the other traits do. Then rivals can guess at traits from how they bid, which is the point of hiding them.
- **Build around it, not a small bonus.** Each trait should change which stance a player reaches for (Attune to
  expand, Summon then Attack to fight, or clearing monsters), not just add points.
- **Trait goals are hidden until the end, and the main score is visible.** So every goal is **capped**, or the
  visible scoreboard would mean little. The caps below are 8–10 points: about 15% of a typical final score (the
  simulator's 3-player bot games end around 55 points: about 40 for land, 12 for mana, 3 for conquest).
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
