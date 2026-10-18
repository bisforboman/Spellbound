# Realm rework: Mageborn against what's built

Status: **decided 18 Oct 2026, not built yet.** The owner's research document "Mageborn — An Archmage-Style Game
Design" proposes a deeper Archmage-style Realm. This page maps it onto the code as it stands and records what the
owner decided.

## Decisions (18 Oct 2026)

1. **Mageborn replaces the Realm**, step by step, in place.
2. **Skirmish is removed entirely**: its code, pages, tables, tests, simulator and docs. (The open questions below
   about Skirmish following the new books no longer apply.)
3. **The rework comes first**; deploying waits until the new Realm's economy and battles exist.
4. **The research's names are adopted**: Mageborn, the schools Dawn, Grove, Ember, Umbra and Veil, the buildings
   (homestead, node, workshop, library, fort, ward) and the rest. They replace the placeholder names.

## The short version

- **The foundation already matches.** The research's prototype section describes this project as it is: a pure C#
  rules core (`Spellbound.Domain`), turns computed lazily from timestamps (`TurnClock`), a thin server-rendered web
  front end, bots on the same rules API and simulation harnesses (`tools/Spellbound.Balance` for the Realm, and the
  Skirmish simulator now removed). The rework is rules content, not architecture.
- **Most of it is new rules, not changed numbers.** The Realm's domain today is about 1,200 lines: two building
  types, three generic units, one battle formula with luck, five self-spells and five hexes. Mageborn's economy,
  creatures, stacked battles, research and season structure are a rebuild of most of that.
- **Nothing to migrate.** Nothing is deployed, and every Realm round resets, so a new ruleset lands at the start of a
  round. Each game already stores the rules it runs under.
- **The decision that shapes everything else:** Skirmish reuses the Realm's schools, spells, units and counter
  bonus (see "The shared core" below). Rebuilding the Realm's units and spells either takes Skirmish along or leaves
  it on today's books.

## Section by section

Sizes are rough: **S** a day or less, **M** a few days, **L** a week or more of work, each with tests and a playtest
step.

| Mageborn | Today | Kind | Size |
|---|---|---|---|
| **Turn bank:** 1 turn / 10 min, cap 150 (25 h), 100 to start | 1 turn / 6 min, cap 240, 100 to start | Numbers in `GameRules` | S |
| **Season of 28 days** in phases: Sanctuary 1–3, Growth 4–10, Contest 11–17, Conjunction 18–27, Reckoning 28 | 28-day rounds, no phases | Change: phases as a function of the day, read by explore and attack rules | M |
| **Vigil:** freeze your realm once a season, up to 72 h | — | New | S |
| **Verbs:** Explore, Build, Study, Cast, Channel, Strike; ledger preview before every commit | Explore, Build, Meditate, Cast, Recruit, Attack, Hex; cost shown, result not projected | Change: a dry-run of each action that the page shows before you confirm (the domain already does all-or-nothing actions, so a preview is "run it on a copy") | M |
| **Resources:** turns, gold, mana, population | turns, gold, mana | New: population (cap from homesteads, spent by recruits and some creatures) | M |
| **Buildings as shares of land:** homestead, node, workshop, library, fort, ward, wilderness; smooth published yield curves with a shaded sweet spot | Towns and nodes, flat yields | Rebuild of the economy (`Mage`, `GameRules`, Build page) | L |
| **Explore decays** with realm size and sharply from day 11; cheaper below the median | `10 × 250 / land` acres a turn | Change: add the day and the median as inputs | S |
| **Five schools on a wheel** (Dawn, Grove, Ember, Umbra, Veil); neighbours cheap, opposites double | Schools A–E with one strong and one weak counter each | Change: a wheel with neighbour and opposite costs; names are the owner's call | S |
| **Research:** Study verb, libraries, four tiers (Cantrip, Rite, Grand, Apex), Spell Level | No research; each school knows its spells from the start | New | L |
| **Spells:** summons, enchantments with upkeep, battle spells, sorcery, one Apex per school; no failure rolls, off-school costs more | One self-spell per school plus a Ward; hexes with a success roll | Rebuild of `Spells.cs`, Magic page | L |
| **Creatures:** per school, class (Flyer, Ground, Ranged), attack, health, initiative, damage type, resistances, one keyword; mundane troops for gold | Three generic units (Unit 1–3) | Rebuild of `Units.cs`, Army page | L |
| **Stacked battle:** your 5 strongest stacks, fixed line-up and pairing, initiative, fatigue, counters; deterministic | Army power × counter × luck against defense × luck | Rebuild of `Mage.Hostile.cs` | L |
| **War Table:** both line-ups, pairing and an outcome band before you strike; scouting ±15 %, blind to Wards | Shows your power, their land's guards and the luck spread | New page section, as text and tables at 360 px | M |
| **Strike kinds:** Assault, Siege, Raid | One attack | Change | M |
| **Range 75–150 %**, retaliation always in range, one hostile act per target per day, Shaken after 3 losses in 24 h, a public Chronicle | Range 60 %, repeat attacks take half the land for 72 h | Change: numbers plus new limits | M |
| **Wards:** up to 3 "if the attacker is X % of me, cast Y" rules plus a standing battle spell | A Ward spell that turns hexes aside | New | M |
| **Guilds** of up to 8, one reinforcing stack | — | New (tables, pages, invites) | L |
| **The Fall:** losing your last fort reincarnates you at half the median land, keeping school and research; Sanctuary, then a Vengeance mark | No death; land just shrinks | New | M |
| **Armageddon:** 5 casts by 5 mages a day apart from day 18, Doomsayers attackable by anyone, then a 24 h Final War without range limits | Rounds simply end | New (a server-wide chain, so a little like `RoundKeeper`) | M |
| **Hall of Fame titles:** Archmage, Sage, Conqueror, Unbroken, Doomsayer; top 10; strongest guild; published net power | Hall of fame by land and networth | Change | S |
| **Omens:** 2 per season, announced before day 1 | — | New (input randomness, a seeded draw at round start) | S |
| **Onboarding:** Tutor's Plan for the first 150 turns, a 7-day Apprentice season against 20 AI mages, school difficulty labels | Create a mage and play | New (the AI mages need a Realm bot) | L |
| **Bots and a sim harness** that replay days 1–7 | `tools/Spellbound.Balance`: bots play the Realm's rules for a round | Change: replay the opening many times with bot strategies | M |

## The shared core

`game-design.md` says both modes share the schools, the spells and the units, and Skirmish reuses them today:

- `SkirmishSeat`, `SkirmishGame` and `SkirmishGame.Conflict` call `UnitBook.All` and `UnitBook.Find` for recruits,
  armies and upkeep, `SpellBook.Find` and `SpellBook.FindHex` for casts and hexes, and `MagicSchool`'s
  `StrongAgainst`, `WeakAgainst` and `CounterSign` for the counter bonus in attacks and hexes.
- `SkirmishSpells` maps the Realm's spells to Skirmish costs; the Skirmish order form and game page list them.
- `tools/Spellbound.SkirmishSim` and its balance results assume today's units and spells.

So rebuilding units into per-school creatures, and spells into tiers, means either:

1. **Skirmish follows** the new books: its sim and tests are re-tuned after each slice (more work, one shared
   world), or
2. **Skirmish keeps today's books**: they move into the Skirmish namespace as its own unit and spell lists, and the
   Realm gets new ones (the modes drift apart, which the owner said may happen anyway).

## Standing decisions it would change

- **"The Realm stays classic"** (game design, Principles) is superseded if this is adopted: Mageborn keeps the Realm
  Archmage-like but replaces battle luck with hidden information and adds Omens.
- **Mobile-first, text-first, 360 px.** The research leaves "mobile or desktop" open; the project decided mobile and
  text. The War Table and the building curves would be tables, numbers and bars, not drawings.
- **Placeholder names.** The research names everything (Mageborn, the five schools, the buildings, creatures). The
  owner asked for placeholder names until names are discussed; are these the names now?
- **What comes first.** The friends beta of Skirmish was next, then the Realm's launch. A Realm rework before any
  deploy delays real players.

## A possible order

Following the research's own playtest plan (economy first, battles second, a closed alpha third). Each step is one
PR with tests, a sim run where it applies, and a playtest step.

1. This page and the decisions below.
2. Turn bank numbers, season phases and published net power, with the Realm simulation (`tools/Spellbound.Balance`)
   extended so every later step is checked by simulation.
3. Buildings as shares, population, yield curves and the ledger preview.
4. The school wheel, research and Spell Level.
5. Creatures, stacked battles and the War Table.
6. Strike kinds, range and hit limits, Shaken, the Chronicle, Wards.
7. The Fall, Vigil and Sanctuary.
8. The Conjunction: Armageddon, the Final War and the titles.
9. Omens, guilds, the Tutor's Plan and the Apprentice season with AI mages.

## Open questions for the owner

1. Replace the Realm with Mageborn, or add it as a third mode?
2. Does Skirmish follow the new schools, spells and units, or keep today's?
3. Skirmish beta first, or the rework first?
4. Are the research's names (Mageborn, Dawn/Grove/Ember/Umbra/Veil, the buildings) decided, or still placeholders?
5. The research's own open questions: 5 stacks or 6–7; items, heroes and gods later; the setting.
