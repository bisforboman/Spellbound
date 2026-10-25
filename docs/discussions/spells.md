# Spells: the first set (decided 20 Oct 2026)

How the first spells were chosen. The owner took all five, with **flat, small costs**. They first chose **Cast as a
fourth stance**; once built, the simulator showed that as a stance spells used the surplus only by replacing Attunes
(bots still ended with 52 mana unspent), so the owner moved them **alongside the stance**, as decision 1 below
recommends (26 unspent). The rules are in [game-design.md](../game-design.md#spells).

**24 Oct 2026: back to a stance.** The owner made Cast a stance again: a Cast round casts one spell instead of the
stance's action, at the same costs; Rally, which needed an attack in the same round, was removed; and the
unpicked-stance discount was removed for every stance. Why go back: one decision a round again ("a stance is one
button"), and spells become a choice that costs a round, not a top-up that every round gets. The known risk was the
surplus this page's first build ran into, and it came back: in 2,000 bot games (3 players), unspent mana at the end
went from about 11 to 28 a seat, spells cast from 19 a game to 2, and Spellweaver's win rate from 29% to 6% (Hexer
34% to 12%). Fixes are proposed to the owner, not built: see the PR that made the change.

## The problem they solve

Mana piles up. Bots finish a match with 50–60 mana unspent and sit on 25–30 by round 4; the simulator never sees a
player priced out of an Attune. The reason is the round's shape, not just the bots: one stance per round, and in an
Attune round (most rounds) the only purchase is one Attune, about 6–8 mana by the middle of the game, against an
income of 8–10. Summon is the only open-ended way to spend, and Imps are the cheap thing to summon, so a surplus turns
into Imp hordes (the biggest stack averages 9 Imps, and has reached 49). So spells should give **a use for mana in the
rounds you don't summon**.

## Two decisions first

**1. A spell is cast alongside the stance, not as a stance.** As with moves: optional, **at most one spell a round**,
committed in secret with the stance. A fourth "Cast" stance would keep "a stance is one button" intact, but you would
cast *instead of* attuning, and the Attune rounds, where the surplus builds, would still bank it. The stance stays the
round's one decision; the spell is an optional extra on the same form, like moves. *(Alternative: Cast as a stance.
Simpler, but it leaves the surplus.)*

**2. Spells change this round, never the economy.** No spell raises a yield, lowers a price or adds income: those
compound and fight principles 12 and 13. A spell changes one battle or one round. Costs are **flat and small** (3–5
mana, about the leftover after an Attune), so a player who casts most rounds spends roughly their income; one spell
(Scorch) scales with how much you pour into it, so a hoard has somewhere to go. *(Alternative: costs that grow with
tiles held, as Attune does, so the leader pays more. Fairer, but harder to read at a glance; a later tuning step if
the simulator shows the leader running away with spells.)*

## The set (5 spells)

Every spell is deterministic, previewed before you commit (cost and effect, like every action), and **in reach**: its
target is one of your tiles or a tile next to your land. Battle spells resolve **after moves, Attune and Summon, and
before attacks**, where defenders can already reinforce and attackers can't be sure what they'll meet.

| Spell | Cost | Target | Effect (this round) | Liked by |
|---|---|---|---|---|
| **Scorch** | 2 mana per power | a rival's tile next to yours | Burns that much of its units' power, weakest first (the battle loss rule). | Warbrand |
| **Ward** | 3 mana | one of your tiles | Its defence counts 3 more power in this round's battles. | Hearthkeeper, Weaver |
| **Leyline** | 1 mana per unit (at least 2) | two of your tiles, any distance apart | Moves units between them, even across a gap; they count as moved. | Wanderer |
| **Blight** | 4 mana | a rival's tile next to yours | It yields nothing in the next round's income. | Wildheart (against a rival's best tile) |
| **Lure** | 3 mana | a monster tile next to yours | Its guards fight with 1 less power this round (3 becomes 2). | Beastbane |

Notes, spell by spell:

- **Scorch** is the scaling sink and the answer to Imp hordes. *Key number:* at 2 mana per power it is dearer than
  summoning an Imp (1 mana), so it is **attrition**: it softens a defence before your attack, it doesn't out-trade a
  horde. At 1 per power it would be a true counter. Monsters are immune (Lure is their spell), so it can't clear a tile
  without fighting.
- **Ward** is the "defender bonus" the battle rules left for later, as something you choose and pay for. Because ties
  already go to the defender, +3 makes a tile hard to take for one round.
- **Leyline** lets scattered realms defend themselves, Wanderer's weakness. Priced per unit so moving a horde costs.
- **Blight** is the cheap, direct rival spell. It lands at the start of the next round, so it is legible in the report.
- **Lure** makes monster tiles reachable with two Imps and a spell instead of a Golem.

## Left out, and why

- **Turning mana into points.** It rewards sitting still, the hoarding the trait pool already turned down.
- **Lasting economy** (more yield, cheaper Attune, free upkeep): it snowballs.
- **Information spells** (see a rival's stance, peek at a dark tile before you commit). Once players commit in secret
  and wait for each other (M2), knowing something before committing breaks the round. A scry could come later if it
  resolves with the round and only informs the next one.

## Costs to the game

- The orders form gains a **Spell (optional)** section, like Moves: a spell, its target, and for Scorch and Leyline how
  much, with the cost and effect in the preview. The 44 px rule and a playtest step apply.
- **Bots must cast too**, or the simulator can't measure the sink. As built: a bot with mana to spare beyond its next
  Attune Wards a threatened tile, Lures a monster its units can then beat, or Scorches a rival tile its units can then
  beat; once it can't attune, it may also Blight a rival's best tile. Leyline isn't in the bots yet, and in the first
  runs they almost never found a Lure or Scorch worth casting, so those three are unmeasured.

## How we'd check

The simulator gains mana numbers: **unspent at the end, spent per stance and per spell**, and Golems against Imps. A
good result: unspent mana at the end falls from 50–60 to near one round's income, Imp stacks shrink, and attacks stay
at least as common as now. Then the costs above get tuned from those numbers.
