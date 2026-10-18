# Mageborn: the design

The owner's research document (18 Oct 2026), brought into the repo as the design record for the Realm's rework. It is
the source of truth for the new rules; `game-design.md` still describes the Realm as it is built today, and
[realm-rework.md](realm-rework.md) maps one onto the other and records the owner's decisions (Mageborn replaces the
Realm; Skirmish is removed; these names are adopted).

## What's built

Each step of the rework adds a line here; the order is in [realm-rework.md](realm-rework.md).

- **The five schools** are named and in wheel order (Dawn, Grove, Ember, Umbra, Veil), with each school's neighbours
  and opposites as data (`MagicSchoolExtensions`). Their effects are still today's (a counter ring in battles and
  hexes, one self-spell each) until the wheel costs and battle steps.
- **The opening study** (`tools/Spellbound.Balance -- --opening`) replays days 1–7 two hundred times with bot
  strategies; [opening-baseline.md](opening-baseline.md) is today's baseline, and each step's diff of it shows what
  that step changed.
- **The ledger preview**: every peaceful action's form (Explore, Build, Meditate, Cast, Recruit) shows what it would
  leave you with, and what would desert or fade, by running it on a copy of the mage (`Mage.Preview`). Strikes get
  theirs from the War Table.

## Summary

Archmage is an asynchronous, text-only browser strategy game from Korea (Mari Telecom, around 2000): you run one mage's kingdom on turns that drip in over real time, grow it with land, buildings and spells, and fight other mages with stacked armies until the season ends in a server-wide Armageddon.

Its style in one line: **spreadsheet-deep kingdom optimisation plus a counter-stacking metagame and guild politics, all governed by one scarce currency: turns.**

Mageborn (working title) keeps that core: the turn bank, a five-school magic wheel, land split into building shares, stacked battles, and seasons that end in Armageddon. It applies the research to fix what made Archmage brutal. Seasons run 4 weeks instead of 2–3 months. The turn cap equals one day, so one login a day loses nothing. Battles are deterministic and previewable. A guided opening replaces the blind start, the Fall replaces permanent death, and several Hall of Fame titles give more than one way to win.

## Part 1 — Archmage analysed

Archmage is a game about spending a trickle of time well: every action costs turns, turns arrive on a fixed clock, and the best mages are the ones who waste none.

### What it was

- **Origin.** Korea's first web strategy game, built by Mari Telecom and funded by banner ads ([NamuWiki](https://en.namu.wiki/w/%EC%95%84%ED%81%AC%EB%A9%94%EC%9D%B4%EC%A7%80)). Purely text, no graphics or sound ([GameFAQs review](https://gamefaqs.gamespot.com/webonly/916235-archmage-reincarnation-from-hell/reviews/58476)).
- **Scale.** Several parallel servers (Apprentice, Guild, Blitz, foreign-language) with thousands of mages each, reset roughly every 3 months.
- **Successors.** After Mari Telecom folded, fans rebuilt it from scratch as [The Reincarnation](https://wiki.the-reincarnation.org/Beginner's_Guide_to_TR); ArchSpace and the blockchain relaunch [Arch Reborn](https://www.archreborn.com/) followed. The rules below come mostly from The Reincarnation wiki, which mirrors the original closely.
- **Not to be confused with** the Starling Games board game *Archmage* (hex exploration, six spheres of magic), which is unrelated.

### The core loop

1. **Turns accrue on a clock.** For example, 1 turn per 15 minutes up to 150 stored on the Apprentice server, 1 per 5 minutes up to 200 on Blitz.
1. **Spend turns on verbs:** explore for land, build, research, cast or summon, recruit, attack, or convert a turn into a double helping of gold ("gelding") or mana ("charging").
1. **Land becomes buildings, buildings become income.** Each building type is a share of your acres: towns and farms give gold and population, nodes give mana and its storage cap, workshops speed building, guilds speed research, forts are your life, barriers resist spells. Returns diminish as a share grows.
1. **Everything has upkeep.** Creatures eat mana, gold or population every turn; run dry and they disband.
1. **Armies fight as stacks.** One stack per unit type; only the 10 largest by power fight. Order and pairing follow fixed rules (flyers front, ranged back, ground units cannot hit flyers), then initiative, resistances, fatigue and a random multiplier decide damage ([Understanding Stacking](https://wiki.the-reincarnation.org/Understanding_Stacking)).
1. **Attacks are instant.** No travel time and no way to dodge; targets must sit within a net-power band (80–200% of yours). Regular attacks and sieges take land; pillages steal and burn.
1. **Defence is pre-programmed.** "Assignment" sets which spell or item fires when an attacker above a chosen size hits you while you sleep.
1. **Seasons end in Armageddon.** Seven mages must each cast it, a day apart, and survive; then a 24-hour war with no range limits ends the server, and the top 10 enter the Hall of Fame. If nobody finishes it, the admins trigger the final war after about two months.

### Style pillars

| Pillar | How it feels in play |
|---|---|
| Scarce time currency | Every session is a budget: "I have 140 turns, what is the best use?" |
| Kingdom as spreadsheet | Ratios and thresholds matter: forts at 2.5% of land, barriers at 2.5%, node share against mana per node. |
| Identity by colour | Five schools on a Magic: The Gathering–style wheel; neighbours are friendly, opposites are costly. |
| Counter-stacking metagame | You read what the server fields and stack against it; "don't stack, counter-stack". |
| Asynchronous, inescapable conflict | You can be hit while offline, so defence is planned, not reacted. |
| Social layer | Guilds, alliances, unwritten rules of engagement (one hit per day), hit-lists and grudges. |
| Imagination over graphics | Battle reports and chronicles in text; the drama is in numbers and names. |
| Seasonal reset and legacy | Everyone restarts each season; only the Hall of Fame persists. |
| Power at a cost | Spells like Nether's soul pact: huge gain, painful price. |

### Through the research lens

Archmage already does the most important thing the research praises: it has one central constraint (turns plus upkeep) that organises a very wide menu, much like feeding in Agricola. Its weaknesses are exactly the research's pitfall list.

| Research pitfall | Where Archmage falls into it |
|---|---|
| Opaque scoring and formulas | Income cliffs at ".99%" shares; a damage formula with six multipliers; players learn from wiki guides, not the game. |
| Unmitigable output luck | Off-colour spells can fail and burn the turns; physical attacks roll a random 0.2–0.8 damage factor. |
| Runaway leaders | NamuWiki's verdict: once on top, always on top, until the reset. |
| Punishing failure states | Zero forts means your mage is dead for the rest of the season. |
| Front-loaded blind decisions | Colour choice and opening land split decide much; Nether is flagged as one of the worst picks for newcomers. |
| Social-only catch-up and kingmaking | Fairness rests on guild norms, hit-lists and guilds ganging up ("BBQ") on a target. |
| Newcomer blowouts | The guide's own advice: join a guild, because one or two seasons there teach more than a year solo. |

## Part 2 — What to keep, what to fix

Keep the skeleton, rebuild the edges: Archmage's core already matches the research's principles, so every change below targets readability, luck, runaway leaders and the first season.

| Research principle | Archmage today | Mageborn response |
|---|---|---|
| One central tension | Turns plus upkeep | Keep, and state it in one sentence (Part 3). |
| Few weighty decisions per action | About 15 menu actions | Group into 3 domains and 6 verbs; every commit shows its projected result first. |
| Hidden expert lever in plain sight | Stack pairing, explore-to-war switch, summon order | Keep all three; make the rules visible but not their value. |
| Multiple viable paths | Mostly net power rank | Five Hall of Fame titles: power, magic, conquest, survival, Armageddon. |
| Input randomness before decisions | Output luck in battle and casting | Deterministic battles and casting; uncertainty comes from hidden information and season Omens. |
| Readable scoring | Hidden formulas, ".99%" cliffs | Published net-power formula; smooth efficiency curves with the sweet spot shaded in the UI. |
| Soften failure states | Death at zero forts | The Fall: reincarnate with half the median land, your spells and a revenge bonus. |
| Built-in, not social, anti-runaway | Guild norms and gang-ups | Tighter range (75–150%), one hit per target per day, "Shaken" immunity, explore cheaper below median. |
| Scripted first game | Apprentice server, guild mentoring | 7-day Apprentice Season against AI mages, plus a guided opening plan. |
| Keep everyone engaged | Log in or lose turns at the cap | Cap equals 25 hours of turns; a Vigil mode freezes the realm for short absences. |
| Cut ruthlessly | 10 stacks, 5 resources, items, heroes, skills, gods, black market | 5 stacks, 3 resources plus turns; items, heroes and gods deferred to expansions. |
| Prototype the opening repeatedly | — | Bot harness that replays days 1–7 thousands of times. |

## Part 3 — Mageborn: the core

Mageborn is a 4-week asynchronous browser strategy game: you are a reincarnated mage ruling a small realm, you spend turns that refill every 10 minutes, and the season ends when five mages complete the Armageddon ritual or the stars force it.

### The central tension

> Every turn and every acre does one job — grow the realm, deepen your magic, or feed the army that guards both — and the army eats what the land makes.

A beginner feels it as "don't let my creatures starve". An expert reads it as an efficiency problem: net power per turn, per acre and per point of upkeep.

### Season structure (28 days)

| Phase | Days | What changes |
|---|---|---|
| Sanctuary | 1–3 | Nobody can attack you unless you attack first. Guided opening runs. |
| Growth | 4–10 | Exploring is cheap; first raids on careless neighbours. |
| Contest | 11–17 | Exploring slows sharply; land now comes mainly from war. |
| Conjunction | 18–27 | Armageddon can be cast; the Final War follows its completion, or day 27 at the latest. |
| Reckoning | 28 | Titles awarded, Hall of Fame updated, server resets. |

A faster **Blitz** server runs 7-day seasons at 1 turn per 3 minutes; a slower **Long** server runs 8 weeks for players who want Archmage's original epic pace.

### The turn bank

- 1 turn every 10 minutes, cap 150 (25 hours). One visit a day never wastes a turn.
- New mages start with 100 turns banked.
- **Vigil:** once per season you may freeze your realm for up to 72 hours (no income, no turns, cannot be attacked or attack).
- Turns are never sold. Paying for turns would break the central tension.

### Verbs

Every action is one verb plus a number of turns. Before you commit, a ledger preview shows the result: acres gained, income after upkeep, which stacks would disband and when.

| Domain | Verb | Per turn |
|---|---|---|
| Grow | Explore | Gain acres of wilderness; yield falls as your realm grows, rises if you are below the server median. |
| Grow | Build | Turn wilderness into buildings at a rate set by workshops. |
| Magic | Study | Progress research at a rate set by libraries. |
| Magic | Cast | Summon creatures, raise an enchantment, or cast sorcery at another mage. |
| Magic | Channel | Take an extra turn's worth of gold or mana income. |
| War | Strike | Attack another mage (Assault, Siege or Raid). |

Free actions cost no turns: setting Wards, reading scouting reports, guild chat, and disbanding.

## The realm: land, buildings, upkeep

Your realm is a pie of acres; how you slice it is the economy, and the slices compete because every acre holds exactly one building.

### Resources

| Resource | Comes from | Cap | Spent on |
|---|---|---|---|
| Turns | The clock | 150 | Every verb |
| Gold | Homesteads × population | None | Building, mundane troops, creature upkeep |
| Mana | Nodes | 1,000 per node | Spells, summons, creature upkeep |
| Population | Homesteads | Set by homesteads | Recruits, "hungry" creatures (Ember, Umbra) |

### Buildings

| Building | Effect | Sweet spot (shaded in UI) |
|---|---|---|
| Homestead | Population cap and gold | 30–40% |
| Node | Mana income and storage | 20–30% |
| Workshop | Build speed, compounding | 10–20%, falls late |
| Library | Research speed | 5–15%, near 0 once research is done |
| Fort | Your life; defence bonus | 2–3% |
| Ward | Spell resistance, up to 75% | 2–3% |
| Wilderness | Nothing yet | As little as possible |

The percentages are starting tuning targets, not tested values.

### Rules that replace Archmage's hidden math

- **Smooth curves, no cliffs.** Each building's yield per acre falls along a published curve; the build screen draws it with the sweet spot shaded.
- **Upkeep is a line, not a surprise.** The realm screen shows net income per turn for gold, mana and population, and a countdown for anything that will disband.
- **Exploring decays by design.** Yield per turn falls with realm size and drops sharply from day 11. Knowing when to stop exploring and start striking is the first expert lever.
- **Batching is honest.** Building 10 turns at once uses the same compounding as 10 single turns, so no one is punished for not micro-clicking (Archmage rewarded building workshops one turn at a time).

## Five schools of magic

Your school is chosen once per season and sets your creatures, spells and economic lean; neighbours on the wheel lend you magic cheaply, opposites only at double cost.

| School | Theme | Leans on | Plays as | First school? |
|---|---|---|---|---|
| Dawn | Angels, unicorns, light | Mana and gold | Defensive, durable, healing | Recommended |
| Grove | Beasts, treefolk, storms | Mana | Ground power, growth, flexible | Recommended |
| Ember | Dragons, fire giants, salamanders | Population | Aggressive, high damage, fragile | Fine |
| Umbra | Undead, demons, pacts | Population and mana | Power at a cost; pacts that pay now and bill later | Hard |
| Veil | Illusions, elementals, minds | Any mix | Widest spell list, tricks, battle control | Hard |

Wheel order (each school's two neighbours): Dawn – Grove – Ember – Umbra – Veil – back to Dawn. Each school opposes the two it does not touch.

### Spells

- **Four tiers:** Cantrip, Rite, Grand, Apex. You can learn your own school to Apex, a neighbour to Grand, an opposite to Rite.
- **No failure rolls.** Off-school casting costs +50% mana and turns from a neighbour, +100% from an opposite. You know the price before you click.
- **Spell Level** rises as you research; it scales summon sizes and spell effects. Completing your school is one Hall of Fame path.
- **Kinds of spell:**
  - Summons: creatures added to your army.
  - Enchantments: lasting self-buffs that cost upkeep every turn.
  - Battle spells: one cast alongside a Strike, or set in Wards for defence.
  - Sorcery: hostile spells at another mage (drain mana, curse income, reveal army). Wards resist them; each counts as your one hostile act against that mage that day.
  - Apex: one signature spell per school, e.g. Umbra's soul pact: a huge army now, a heavy population debt for 48 hours.
  - Armageddon: the season's end-ritual (see Conjunction).

## Armies and battle

Battles keep Archmage's best idea — armies as ordered stacks that pair up by fixed rules — but with 5 stacks instead of 10 and no dice, so a loss is always explainable.

### Creatures

Each creature type has: class (Flyer, Ground or Ranged), attack, health, initiative 1–5, a damage type (Physical, Fire, Frost, Holy, Shadow or Mind), resistances, and at most one keyword such as Swift, Siege, Regenerate, Fear or Undying. Mundane troops (archers, pikemen) are recruited with gold; everything else is summoned with mana.

### How a battle resolves

1. **Line-up.** Your 5 most powerful stacks fight. They line up by class — Flyers in front, Ground in the middle, Ranged at the back — then by size within each class. You shape the line-up only through what you build.
1. **Pairing.** Each stack targets the enemy stack in the same position. Ground stacks cannot hit Flyers, so they take the first unpaired stack they can reach. Stacks with no target left start again from the enemy's front.
1. **Strikes.** Stacks strike in initiative order, highest first; ties go to the defender. Damage = count × attack × (1 − resistance) × efficiency.
1. **Fatigue.** Each time a stack is struck after the first, its efficiency drops 15%. A tiny decoy stack can soak a big hit and tire the enemy — the classic "fake" and "fatigue" stack tricks survive.
1. **Counters.** Struck Ground stacks hit back at half strength if the attacker is in reach.
1. **Outcome.** The attacker wins if they destroyed at least 10% of the defender's power and a larger share than they lost.

### The War Table

Before a Strike, the War Table draws both line-ups, the pairing lines and an expected outcome band. Your view of the enemy comes from scouting: exact for your own army, ±15% on enemy stack sizes, and blind to their Wards. The battle itself is deterministic; the uncertainty is what you don't know about them, not a dice roll.

## War, defence and the Fall

War stays instant and inescapable, but the rules — not guild etiquette — now limit how hard and how often anyone can be hit.

### Strikes

| Strike | Needs | On a win | Cost |
|---|---|---|---|
| Assault | Any army | Take up to 6% of their land; you keep half as wilderness, the rest is ruined | One turn of your whole army's upkeep |
| Siege | Flyers or Siege units do full damage; others at half | Up to 10% of land and one of their forts | Same, and the defender's fort bonus doubles |
| Raid | Count of units, not power | Steal gold and mana, burn up to 5% of buildings; no land | No upkeep cost; fails outright if blocked |

### Limits built into the rules

- **Range:** you may strike mages with 75–150% of your net power. Anyone who struck you in the last 24 hours is always in range for one retaliation.
- **One hit per target per day:** you may make one hostile act (Strike or sorcery) against the same mage per 24 hours. Archmage's most common etiquette rule becomes a rule.
- **Shaken:** a realm that loses 3 battles within 24 hours is immune to attack for the next 12 hours.
- **Chronicle:** every hostile act is published in a server feed with attacker, target and result.

### Defence while you're away: Wards

Wards are pre-set orders, carried over from Archmage's assignment: up to 3 rules of the form "if the attacker's army is at least X% of mine, cast Y", plus one standing battle spell. Wards cannot be blocked, and the ledger shows how many triggers your mana can afford. Tuning X so small probes can't drain you is an expert skill.

### Guilds

- Up to 8 members; alliances only within a guild.
- An ally reinforces one battle with one stack, at reduced strength.
- Guild chat, a shared Chronicle filter and a guild title at the Reckoning. The cap keeps any one guild from owning a server.

### The Fall (instead of death)

When your last fort is taken, your realm falls — and, true to the setting, you reincarnate:

- Within an hour you start a new realm with 50% of the server's median land, guided build split applied.
- You keep your school, research and Spell Level; you lose army, gold and mana.
- 48 hours of Sanctuary, then a **Vengeance** mark: +20% damage against whoever felled you, for 7 days.
- You lose eligibility for the Unbroken title (see below). A Fall is a setback, never a season-ending exit.

## The Conjunction and how to win

The season builds to one climax — Armageddon and a 24-hour Final War — but five separate titles mean a mage who isn't top in power still has something worth chasing.

### Armageddon

- From day 18, any mage who knows every Grand spell of their own school may cast Armageddon.
- It needs 5 casts by 5 different mages, at least 24 hours apart. One cast per day, server-wide.
- Casters are secret, but each becomes a **Doomsayer**: anyone may strike them regardless of range. A Doomsayer who Falls before the Final War breaks their link, and the chain needs a replacement.
- When the fifth cast lands, the **Final War** starts 24 hours later. If the chain is incomplete, the stars force the Final War on day 27.
- During the Final War's 24 hours, range limits are lifted. One-hit-per-day and Shaken still apply, so nobody is erased in an hour.

### Hall of Fame titles

| Title | Awarded for | Rewards |
|---|---|---|
| Archmage | Highest net power at the Reckoning | Raw strength |
| Sage | Highest total Spell Level | Research and magic depth |
| Conqueror | Most land taken in battle across the season | Aggression |
| Unbroken | Highest net power among mages who never Fell | Steady, defensive play |
| Doomsayer | Every Armageddon caster who survives the Final War | Daring and timing |

The top 10 by net power and the strongest guild (sum of its top 5 members) are also recorded.

**Net power is published:** land + buildings + army power + Spell Level, each with a printed weight, so anyone can check a ranking by hand.

### Season Omens

Each season draws 2 Omens, announced before day 1, that shift the metagame. Examples: *Long Night* (Umbra spells cost 25% less), *Fertile Age* (exploring yields +30% until day 14), *Skyfall* (Flyers lose their front-line priority). Omens are input randomness: known in advance, planned around, and the main reason two seasons don't play the same.

## Onboarding and the expert levers

A new mage should survive week one by following the game's own advice; a veteran should still be discovering levers in season three.

### First season, designed in

1. **Apprentice Season (optional, recommended).** 7 days against 20 AI mages at Blitz speed. It is Mageborn's "Family game": a real season, not a tutorial, with Apex spells and Armageddon switched off.
1. **The Tutor's Plan.** For the first 150 turns a quest line suggests each action and builds a sound land split, like Catan's beginner map. Following it pays a small bonus; you can leave it at any time.
1. **Good first schools.** Dawn and Grove are tagged as beginner-friendly at school choice; Umbra and Veil carry a "hard" label.
1. **Plain-language consequences.** Every commit previews its effect in a sentence: "Next 10 turns: +4,200 gold, −310 mana; your Unicorns disband in 22 turns."
1. **72-hour Sanctuary** at season start and after a Fall.

### Levers hidden in plain sight

All of these are legal and visible from day one; none is explained as a strategy. Playtests should check that most players find at least two by their third season.

| Lever | What the expert knows | Research parallel |
|---|---|---|
| The switch | When to stop exploring and start striking, before yields collapse | Small World's decline timing |
| Pairing control | Decoy and fatigue stacks; a soaker placed opposite their best stack | 7 Wonders' hate-draft |
| Summon order | Summon cheap-upkeep creatures first and the costliest last, so upkeep starts late | Agricola's feeding engine |
| Ward thresholds | Set triggers so small probes can't drain your mana | Catan's trade refusal |
| Reading the meta | Counter-stack what the Chronicle shows is popular | 7 Wonders' reading neighbours |
| One borrowed Grand spell | A single neighbour-school spell that covers your school's weakness | Catan's ports |
| Doomsayer timing | Cast Armageddon when you can survive four days as a target | Small World's last-turn scoring |

## Risks, prototype and playtests

The biggest risk is that fixing Archmage's harshness also removes its bite; the prototype should test the economy and battles with bots long before anyone plays.

### Risks

| Risk | Mitigation |
|---|---|
| The War Table "solves" battles | Scouting is ±15% and blind to Wards; the defender's battle spell stays hidden. |
| 4-week seasons feel less epic | Offer the 8-week Long server alongside. |
| Thin servers early on | AI mages fill each server to at least 50 realms. |
| Spreadsheet fatigue | Ledger previews, a "build toward target split" helper, and published formulas. |
| Leader-bashing in the Final War | One hit per day and Shaken still apply; four of five titles don't need rank 1. |
| One school dominates | Omens rotate the meta; bot tournaments flag win-rate gaps before release. |
| Pay-to-win pressure | Never sell turns, power or land; cosmetics and server hosting only. |

### Prototype

- **Engine-agnostic C# core:** rules, the turn clock (computed lazily from timestamps, no tick server), the economy and a deterministic battle simulator, all unit-testable.
- **Thin web front end** (for example ASP.NET Core with server-rendered pages) — text-first, as Archmage was; art can come later.
- **Bots on the same rules API** as players: they fill servers and drive balance runs.
- **Simulation harness:** replay days 1–7 thousands of times with bot strategies, the digital version of Rosenberg playing only Agricola's first four rounds.

### Playtest plan

1. Spreadsheet economy: one solo realm over 7 days; does the Tutor's Plan beat random play by a clear margin?
1. Battle CLI: 5 schools × 8 creatures, bot tournaments until no school's win rate strays far from the rest.
1. Closed alpha: about 20 people, one 14-day season.
1. Measure: share of new players still active on day 7; how many use decoy stacks or the switch by season 2; net-power gap between rank 1 and the median at day 21; Final War outcomes.

### Open questions

- Setting and name: keep the reincarnated-mage premise, or a fresh world?
- Is 5 stacks enough depth, or does 6–7 restore the deep-stacking play?
- Should items, heroes and gods return as a first expansion?
- Mobile-first layout, or desktop-first like the original?

## Sources

- [Archmage — NamuWiki](https://en.namu.wiki/w/%EC%95%84%ED%81%AC%EB%A9%94%EC%9D%B4%EC%A7%80): origin, Mari Telecom, five colours, net-power range, Armageddon rules, successors.
- [Beginner's Guide to TR — The Reincarnation wiki](https://wiki.the-reincarnation.org/Beginner's_Guide_to_TR): turn rates, buildings, income, attacks, assignment, etiquette.
- [Understanding Stacking — The Reincarnation wiki](https://wiki.the-reincarnation.org/Understanding_Stacking): stacks, ordering, pairing, combat formula, metagame.
- [Archmage: Reincarnation from Hell review — GameFAQs](https://gamefaqs.gamespot.com/webonly/916235-archmage-reincarnation-from-hell/reviews/58476): servers, text presentation, resets.
- [Arch Reborn](https://www.archreborn.com/): the modern relaunch.
- *Easy to Learn, Hard to Master: Design Lessons from Agricola, Catan, 7 Wonders and Small World* (the attached research).
