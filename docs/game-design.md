# Spellbound game design (draft)

The numbers below are the defaults in `GameRules`. They are first guesses that need balancing through play.

## The pitch

You are a mage in a world of rival mages. A small pool of **turns** builds up while you're away. You spend them in
short sessions on your phone: explore new land, build nodes and towns, meditate for mana, and cast spells. Later
you attack rivals. A round lasts a few weeks and then the world resets.

Design goals:
- **Five minutes a few times a day** is a complete play session.
- **Text-first.** It runs on any phone browser, needs no art budget, and battle reports are words.
- **Fair to async players.** Turns are the universal currency, so checking in constantly gives no advantage beyond the turn cap.
- **No pay-to-win.**

## Core loop

1. Turns regenerate over time, up to a cap.
2. Every action costs turns. Time only moves for your mage when you spend turns. Each spent turn runs one
   economy tick: gold income, mana income, and spell durations count down.
3. You grow land, then buildings, then income, which lets you cast stronger spells and (from milestone 2) field armies and attack.

## Resources

| Resource | Source | Limit |
|---|---|---|
| **Turns** | 1 per 6 minutes (real time) | Max 200 on hand; time at the cap is lost |
| **Land** (acres) | Exploring | None, but each explore turn yields less as land grows |
| **Gold** | Every turn: 1 per acre + 10 per town | None |
| **Mana** | Every turn: 10 per node; meditating adds 100 per turn | 5,000 + 1,000 per node |

## Starting mage

250 acres, 20 nodes, 40 towns (so 190 acres are free), 10,000 gold, 2,000 mana, 100 turns.

New mages are **protected** from attacks until they have spent 120 turns. This matters from milestone 2.

## Actions (milestone 1)

| Action | Turn cost | Effect |
|---|---|---|
| **Explore** | Chosen by the player | Each turn gives `max(1, 10 × 250 / currentLand)` acres, so 10 at the start, 5 at 500 acres and 1 at 2,500 or more |
| **Build** | `ceil(count / 10)` | Turns free acres into nodes or towns. Costs 500 gold per building |
| **Meditate** | Chosen by the player | +100 mana per turn on top of normal income, capped by storage |
| **Cast (self) spell** | Per spell | Costs mana and gives a timed buff, measured in your own spent turns |

Within one spent turn, the action happens first, then the economy tick, then effect durations count down.
New buildings count towards income during the turns spent building them.

## Magic schools

You pick one of five schools when you create your mage. The schools form a ring: each one is strong against the
next and weak against the previous one. The counter bonuses are used by offensive spells and combat from milestone 2.

`Radiant → Verdant → Tidal → Ember → Umbral → Radiant`

### Milestone 1 self-spells (one per school, known from the start)

| School | Spell | Mana | Turns | Duration | Effect |
|---|---|---|---|---|---|
| Radiant | Blessed Harvest | 1,500 | 2 | 30 turns | Gold income ×1.25 |
| Verdant | Overgrowth | 1,500 | 2 | 30 turns | Explore yield ×1.5 |
| Tidal | Wellspring | 1,200 | 2 | 30 turns | Mana income ×1.3 |
| Ember | Forge Fire | 1,500 | 2 | 30 turns | Building cost ×0.8 |
| Umbral | Night Whispers | 1,200 | 2 | 30 turns | Meditation yield ×1.5 |

Recasting an active spell refreshes its duration; buffs don't stack.

## Later (not designed yet)

- **Milestone 2, PvP:** units (recruit, upkeep), attacks that take land, offensive spells against a target
  (mana drain, plague, fog), barriers, battle reports, a rankings list by land or networth, and protection.
- **Milestone 3, rounds:** round length, end-of-round event, reset, hall of fame.
- **Later:** research (new spells, artifacts), guilds, server variants (fast and slow turns, solo or team).

## Open questions

- Should we add a stored-turns bank (as Earth Empires does) so missing a day hurts less?
- Should we add population or peasants, or keep the economy as just land and buildings?
- What does networth count, for rankings?
- How long is a round: 4 weeks or 8?
