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
| **Turns** | 1 per 6 minutes (real time) | Max 240 on hand (one day's worth); time at the cap is lost |
| **Land** (acres) | Exploring | None, but each explore turn yields less as land grows |
| **Gold** | Every turn: 1 per acre + 10 per town | None |
| **Mana** | Every turn: 10 per node; meditating adds 100 per turn | 5,000 + 1,000 per node |

## Starting mage

250 acres, 20 nodes, 40 towns (so 190 acres are free), 10,000 gold, 2,000 mana, 100 turns.

New mages are **protected** from attacks for their first **72 hours** and until they have spent **120 turns**,
whichever comes later. This matters from milestone 2.

## Actions (milestone 1)

| Action | Turn cost | Effect |
|---|---|---|
| **Explore** | Chosen by the player | Each turn gives `max(1, 10 × 250 / currentLand)` acres, so 10 at the start, 5 at 500 acres and 1 at 2,500 or more |
| **Build** | `ceil(count / 10)` | Turns free acres into nodes or towns. Costs 500 gold per building |
| **Meditate** | Chosen by the player | +100 mana per turn on top of normal income, capped by storage |
| **Cast (self) spell** | Per spell | Costs mana and gives a timed buff, measured in your own spent turns |

Within one spent turn, the action happens first, then the economy tick (income, then army upkeep), then effect durations count down.
New buildings count towards income during the turns spent building them.

## Magic schools

You pick one of five schools when you create your mage. The schools form a ring: each one is strong against the
next and weak against the previous one. The counter bonuses are used by offensive spells and combat from milestone 2.

`Radiant → Verdant → Tidal → Ember → Umbral → Radiant`

### Self-spells (one per school, known from the start; see Hexes and barriers for Arcane Barrier)

| School | Spell | Mana | Turns | Duration | Effect |
|---|---|---|---|---|---|
| Radiant | Blessed Harvest | 1,500 | 2 | 30 turns | Gold income ×1.25 |
| Verdant | Overgrowth | 1,500 | 2 | 30 turns | Explore yield ×1.5 |
| Tidal | Wellspring | 1,200 | 2 | 30 turns | Mana income ×1.3 |
| Ember | Forge Fire | 1,500 | 2 | 30 turns | Building cost ×0.8 |
| Umbral | Night Whispers | 1,200 | 2 | 30 turns | Meditation yield ×1.5 |

Recasting an active spell refreshes its duration; buffs don't stack.

## Army (milestone 2)

Every mage can recruit the same three units. Recruiting takes `ceil(count / 50)` turns.

| Unit | Attack | Defense | Gold | Upkeep (gold per turn) |
|---|---|---|---|---|
| Militia | 1 | 3 | 60 | 1 |
| Soldiers | 3 | 3 | 150 | 2 |
| Knights | 6 | 2 | 250 | 3 |

- **Upkeep** is paid every turn you spend, right after that turn's income. If you can't pay it all, your gold drops to 0
  and 10% of every unit type deserts (rounded up) that turn.
- **Defense** is your units' defense plus **2 per acre** (the land's own guards), so a mage with no army isn't free land.

## Attacks (milestone 2)

An attack costs **4 turns** and resolves in the first of them; the 4 turns then run their economy ticks as usual.

- **Who you can target:** not yourself, and not a protected mage. While *you* are protected you can't attack or hex
  anyone either. The target must hold at least **60%** of your land, so big mages can't farm small ones.
- **Power:** attack = your units' attack × school counter (×1.15 if your school is strong against theirs, ×0.85 if
  weak) × luck. Defense = their defense × luck. Luck is a roll between ×0.9 and ×1.1 for each side.
- **Win** (attack > defense): you take **10%** of their land (at least 1 acre), halved for every battle they have lost
  in the last **72 hours**, to anyone: 10%, then 5%, then 2.5%. Each loss is forgotten three days after it happened. Battles
  they held don't count. The target's page shows the share a win would take now. Taken land comes from their free acres
  first; if that's not enough, buildings on it are destroyed, split between nodes and towns by how many they have.
  The land arrives as free acres for you.
- **Losses:** the attacker loses 8% of every unit on a win and 12% on a loss. The defender loses 10% when broken and
  5% when holding.
- Both sides get a battle report. The defender's lands in their news log as a hostile entry.

Randomness comes from an injected, seedable source, so the same seed replays the same battle.

## Hexes and barriers (milestone 2)

Each school knows one **hex**, an offensive spell cast at another mage (same targeting rules as attacks).

| School | Hex | Mana | Turns | Effect on success |
|---|---|---|---|---|
| Radiant | Smite | 2,000 | 2 | Kills 5% of every unit type |
| Verdant | Locust Swarm | 2,000 | 2 | Destroys 8% of their gold |
| Tidal | Mana Drain | 2,000 | 2 | Takes 10% of their mana; you keep what fits in your storage |
| Ember | Firestorm | 2,000 | 2 | Burns 3% of their buildings |
| Umbral | Nightmare | 2,500 | 2 | They lose 3 turns on hand |

- **Chance to land:** 70%, +15 points if your school is strong against theirs, −15 if weak.
- **Arcane Barrier** is a self-spell every school knows: 1,000 mana, 2 turns, lasts 30 of your turns, and halves the
  chance of hexes cast at you. Like every buff, it only counts down as you spend turns.
- A hex that is turned aside still costs its mana and turns. The target hears about it either way.

## Protection, rankings and networth (milestone 2)

- New mages are protected for **72 hours** and until they have spent **120 turns** (see Starting mage). Mages created
  before the time rule existed are protected by turns only.
- Because everyone creates a mage when a round starts, every round opens with about **three days without attacks**.
- **Networth** = 5 per acre + 10 per building + 1 per point of unit attack and defense (a knight adds 8).
- The **Rivals** page ranks every mage by land or by networth, and is where you pick a target.
- When you are attacked or hexed, the overview shows a banner with how many times since you last looked, and the
  entries are marked in the news log. Web push notifications are not built yet.

## Rounds (milestone 3)

- A round lasts **4 weeks** (`RoundLength`). The first one starts when the server first starts.
- When it ends, actions stop. Within a minute the server records the **hall of fame**: the top 10 mages by land and
  by networth, with their school. Then the next round starts right away.
- **Everything resets:** every player creates a new mage with the starting values, and news starts empty.
  Accounts carry over; mages, armies and spells don't.
- The overview shows the round number and the time left. Past rounds are on the Hall of fame page.
- An admin can move the current round's end, or end it now (which runs the reset at once).

## Bans (milestone 3)

An admin can ban a player with a reason. A banned player can't sign in or act, their mage disappears from Rivals and
can't be targeted, and they're left out of the hall of fame. Unbanning restores all of it; the mage is untouched.

## Later (not designed yet)

- research (new spells, artifacts), guilds, server variants (fast and slow turns, solo or team),
  web push notifications for attacks.

## Open questions

A simulation of a full round answers several of these with numbers, and proposes changes: see
[balance.md](balance.md).

- Should we add population or peasants, or keep the economy as just land and buildings?
- Should there be a limit on how often one mage can be attacked, since tiny attacks still cost the defender units?
  See [balance.md](balance.md).
- Is upkeep too harsh, or too light? In the simulation attackers spend nearly all their income on it and still win,
  because stolen land pays for it. Revisit after the repeat-attack question.
- Should there be a break between rounds, or an end-of-round event in the last days (for example, double land taken)?
