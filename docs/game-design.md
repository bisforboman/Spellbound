# Spellbound game design (draft)

The numbers below are the defaults in `GameRules`. They are first guesses that need balancing through play.

**Names are placeholders.** Schools, spells, hexes, units and Skirmish's terrains and cards have deliberately bad names
("School A", "Hex C", "Unit 3") until we decide them together: see [Names to decide](#names-to-decide).

## The pitch

You are a mage in a world of rival mages. A small pool of **turns** builds up while you're away. You spend them in
short sessions on your phone: explore new land, build nodes and towns, meditate for mana, and cast spells. Later
you attack rivals. A round lasts a few weeks and then the world resets.

Design goals:
- **Five minutes a few times a day** is a complete play session.
- **Text-first.** It runs on any phone browser, needs no art budget, and battle reports are words.
- **Fair to async players.** Turns are the universal currency, so checking in constantly gives no advantage beyond the turn cap.
- **No pay-to-win.**
- **A board-game feel** (in Skirmish; the Realm stays classic, see Principles). Strategy and min-maxing matter, but luck is a real part of the game: even a great plan can be
  shafted, as in Catan or 7 Wonders. Prefer luck that comes *before* a decision (you see what you got and plan around
  it) over luck that decides *after* you've committed. Over a long game, luck should come as many small rolls that even
  out, or with ways to recover. Odds are shown before the player confirms, like costs.
- **Art later.** Text-first until the core is fun; art can be reconsidered then.

## Modes (decided 18 Oct 2026; Skirmish is playable: players set up and join games)

Spellbound has two modes that share one core (mages, schools, spells, turns, armies):

- **Realm**: the game described below. An open world without a map, land grows without limit, long rounds;
  closest to Archmage.
- **Skirmish**: a short game on a limited, shared board for a fixed number of players, played in rounds that everyone
  plans at the same time and readies up for (with a time limit and reminders), with a clear end and scoring from several
  sources, like a board game. *Not designed yet*: see [skirmish-design.md](skirmish-design.md).

Players **create and join games**, and many run at once; the Realm can be one always-on game in the same list. A mode
is a preset of **rule switches** (for example "exploring finds things", "attacks on"), and whoever creates a game may
change them within limits, like house rules. Every game stores the rules it runs under, and its page lists them so
players know what they joined.

### Principles

- **One shared core, separate land and clocks.** Both modes share accounts and bans, the win record, the schools with
  their spells and hexes, the units and the battle formula, showing odds before you commit, and replayable randomness.
  Each mode has its own clock (the Realm's regenerating turns, Skirmish's ready-up rounds), its own land (acres, or
  regions on a board) and its own way of acting (instant actions, or orders that play out together). So Skirmish gets
  its own kind of mage that reuses the spell and unit books, rather than the Realm's mage with optional parts.
- **The Realm stays classic.** It keeps its small battle and hex luck and stays otherwise predictable, like Archmage.
  The board-game luck (discoveries, drafts, events) is what makes Skirmish different.
- **Skirmish is the lab.** It's the explorative mode, and the first thing to beta-test with friends. Short games make
  ideas cheap to try; what works there can move to the Realm as a switch. The two modes may grow into completely
  separate games, so the shared core stays small and nothing in one mode should depend on the other.
- **Small communities first.** A Skirmish must be fun for 2 or 3 friends and never wait for a crowd: 2 players is
  enough to start one. The game must work on a server with ten accounts.
- **Each Realm round is a game.** As now, a Realm round lasts 4 weeks and ends with a hall of fame, and the server
  starts the next one. Each round is one entry in the list of games.
- **The Games page comes first.** After signing in, players see their games (their Realm mage and their Skirmishes)
  and the ones they can join; the Realm is one tap away.
- **Open list plus invite codes.** A Skirmish's creator makes it public (listed for anyone) or private (joined with a
  code or link). Anyone can create one, with a cap (for example one open game per player). In a game with few seats,
  one person holding two of them is decisive, so the multi-account signals must also be shown per game.

Until both modes exist, everything below describes the Realm.

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

`School A → School B → School C → School D → School E → School A`

### Self-spells (one per school, known from the start; see Hexes and wards for the Ward spell)

| School | Spell | Mana | Turns | Duration | Effect |
|---|---|---|---|---|---|
| School A | Self-spell A | 1,500 | 2 | 30 turns | Gold income ×1.25 |
| School B | Self-spell B | 1,500 | 2 | 30 turns | Explore yield ×1.5 |
| School C | Self-spell C | 1,200 | 2 | 30 turns | Mana income ×1.3 |
| School D | Self-spell D | 1,500 | 2 | 30 turns | Building cost ×0.8 |
| School E | Self-spell E | 1,200 | 2 | 30 turns | Meditation yield ×1.5 |

Recasting an active spell refreshes its duration; buffs don't stack.

## Army (milestone 2)

Every mage can recruit the same three units. Recruiting takes `ceil(count / 50)` turns.

| Unit | Attack | Defense | Gold | Upkeep (gold per turn) |
|---|---|---|---|---|
| Unit 1 | 1 | 3 | 60 | 1 |
| Unit 2 | 3 | 3 | 150 | 2 |
| Unit 3 | 6 | 2 | 250 | 3 |

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

## Hexes and wards (milestone 2)

Each school knows one **hex**, an offensive spell cast at another mage (same targeting rules as attacks).

| School | Hex | Mana | Turns | Effect on success |
|---|---|---|---|---|
| School A | Hex A | 2,000 | 2 | Kills 5% of every unit type |
| School B | Hex B | 2,000 | 2 | Destroys 8% of their gold |
| School C | Hex C | 2,000 | 2 | Takes 10% of their mana; you keep what fits in your storage |
| School D | Hex D | 2,000 | 2 | Burns 3% of their buildings |
| School E | Hex E | 2,500 | 2 | They lose 3 turns on hand |

- **Chance to land:** 70%, +15 points if your school is strong against theirs, −15 if weak.
- The **Ward spell** is a self-spell every school knows: 1,000 mana, 2 turns, lasts 30 of your turns, and halves the
  chance of hexes cast at you. Like every buff, it only counts down as you spend turns.
- A hex that is turned aside still costs its mana and turns. The target hears about it either way.

## Protection, rankings and networth (milestone 2)

- New mages are protected for **72 hours** and until they have spent **120 turns** (see Starting mage). Mages created
  before the time rule existed are protected by turns only.
- Because everyone creates a mage when a round starts, every round opens with about **three days without attacks**.
- **Networth** = 5 per acre + 10 per building + 1 per point of unit attack and defense (a Unit 3 adds 8).
- The **Rivals** page ranks every mage by land or by networth, and is where you pick a target.
- When you are attacked or hexed, the overview shows a banner with how many times since you last looked, and the
  entries are marked in the news log. Web push notifications are not built yet.

## Rounds (milestone 3)

- A round lasts **4 weeks** (`RoundLength`). The first one starts when the server first starts. Each round is a game
  in the Games list, and keeps the rules it started with; changed settings apply from the next round.
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

## Names to decide

Everything players see with flavour has a placeholder name, so nothing is committed to by accident. The ids stored
with a game (`MagicSchool.A`, `"hex-c"`, `"unit-3"`) stay neutral, so naming them later changes only the display text,
never saved data. Where each name lives:

| Placeholder | What it is | Where to rename it |
|---|---|---|
| School A–E | The five schools, a ring: each is strong against the next | `MagicSchoolExtensions.DisplayName` |
| Self-spell A–E | Each school's own buff (gold, exploring, mana, building cost, meditation) | `SpellBook.All` (name and description) |
| Ward spell | The buff every school knows: halves hexes against you | `SpellBook.All` |
| Hex A–E | Each school's hex (kill units, destroy gold, take mana, destroy buildings, take turns) | `SpellBook.Hexes` |
| Unit 1–3 | The three units: cheap defender, balanced, attacker | `UnitBook.All` |
| Terrain 1–4, Card 1–5 | Skirmish's terrains and discovery cards (not built) | [skirmish-design.md](skirmish-design.md) |

Not placeholders: the game's name (Spellbound) and the modes (Realm and Skirmish). Descriptions say what a thing does,
plainly; flavour text can come with the names.

