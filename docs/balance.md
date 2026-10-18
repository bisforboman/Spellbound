# Balance findings

Nobody has played a full round yet, so these come from a simulation: bots playing Spellbound's real rules
(`Spellbound.Domain`, default `GameRules`) for a 28-day round. The raw numbers are in
[balance-results.md](balance-results.md); to regenerate them after changing a rule:

```sh
dotnet run --project tools/Spellbound.Balance -- [--days 28] [--seed 7]
```

The bots (`tools/Spellbound.Balance/Bots.cs`) act only through the game's own actions and spend every turn they have
at each check-in, three times a day or once a day:

- **Builder:** explores, and builds towns and nodes (1 in 4 a node), and keeps its school's buff up.
- **Turtle:** the same, plus one Unit 1 per acre and a Ward spell.
- **Warmonger:** the same, plus half its spare gold into Unit 3. It attacks a target it is certain to beat as long as
  the land a win would take is at least what exploring would give for the same 4 turns, starting with the target that
  gives the most land.
- **Idle:** signs up and never plays.

**Limits:**
- Real players won't play this perfectly or this greedily.
- The warmonger sees its targets' true defense, which real players can't, so these results are the worst case for
  defenders.
- Hexes, Meditate and most spells aren't used.

The conclusions hold across seeds 1–3; the exact numbers move a few percent.

## Changes adopted after the first run

The first run (rules as of PR #13) found four problems. The owner adopted three proposals (PR #14), and after the
second run a 72-hour memory for the repeat-attack rule:

| Problem in the first run | Change |
|---|---|
| Peers farmed each other: one warmonger hit another 29 times, up to 5 times a day, taking 12,771 acres | Each battle a mage lost in the last 24 hours, to anyone, halves the land the next win takes (10%, 5%, 2.5%, …); later 72 hours |
| Protection lasted hours: the first attacks came on day 1 | New mages are also protected for 72 hours |
| Checking in once a day lost 1,180 turns (17%) at the 200-turn cap | The turn cap is 240, one day of turns |
| Upkeep didn't limit attackers (95–100% of income, still winning) | No change, as proposed: revisit after the others |

## Second run: what changed (seed 7, 24-hour memory)

| | First run | Second run |
|---|---:|---:|
| First attack | day 1 | **day 4** |
| Turns a once-a-day player loses over the round | 1,180 (17%) | **60 (0%)** |
| Once-a-day builder, peaceful server | 5,825 acres | **6,834** (the 3×-a-day builder: 6,785) |
| Busy warmonger / once-a-day warmonger, mixed server | 10,526 / 6,265 | 10,784 / 6,098 |
| Turtle, builders, mixed server | 3,339–3,554 | 3,438–3,565 |
| Warmonger-3x on Warmonger-1x | 29 hits, 12,771 acres | 50 hits, 9,631 acres |
| Most hits on one target in one day | 5 | 8 |

The first run's warmonger always attacked the biggest target in range. Under a flat 10% any target in range was worth
far more than four turns of exploring, so the new rule (attack only when it beats exploring) changes nothing about
that run.

### 1. Protection and the turn cap work

The first attacks moved from day 1 to day 4 on every seed, and a once-a-day player now loses almost nothing. On a
peaceful server they end level with someone who checks in three times a day.

Because everyone creates a mage when a round starts, **every round now opens with about three days without attacks.**
That is a quiet building phase, which fits the game, but it is a side effect worth knowing.

### 2. The repeat-attack rule caps the damage per day, not the war

A beaten mage can lose at most about 20% of its land in a day (10 + 5 + 2.5 + …), where before five hits took 41%.
But the totals barely moved: the busy warmonger still ends with about three times everyone else's land, and the
builders and the turtle end where they did. Two reasons:

- **Attacking stays worth it for big mages.** Exploring yields less as land grows. At 6,000 acres, four turns of
  exploring give about 2 acres, so even a 0.3% take of a big target beats it. The halving makes each hit smaller,
  so the bots attack more often (most hits in a day went from 5 to 8).
- **A day is a short memory.** The rule resets every day, and the warmongers attack every day.

This is "the rule caps damage, not war between peers": a better floor for the defender, not a fix for attacking
dominating.

The once-a-day builder still ends far behind the peaceful run: it builds no defense, so two attackers keep taking
their daily share. That is the rule working as intended on an undefended player.

### 3. New: many small attacks still hurt

The halving shrinks the land, not the attack. Every lost battle still costs the defender 10% of its units and a
hostile news alert. A mage that only takes 1–10 acres per hit can still strip a defender's army and flood their news.
Real players may also do it on purpose, to grief.

### 4. A 72-hour memory does much better (adopted)

Tried by changing only `RepeatAttackWindow` to 72 hours (seeds 7, 1 and 2), and then made the default. The current
[balance-results.md](balance-results.md) is this run:

| | 24 hours | 72 hours (now the default) |
|---|---:|---:|
| Warmongers | 5,726–11,607 | 4,346–6,482 |
| Turtle | 3,565–3,739 | 5,423–5,489 |
| Builders | 3,379–3,953 | 4,933–6,053 |

With a three-day memory the attackers end level with the builders, and a builder ends close to its peaceful size
(about 6,000 acres). Attacks continue all round, but each takes little. A steeper halving (×0.25 per loss, 24 hours, seed 7)
helped less: builders 3,835–4,148, warmongers 7,025–8,652.

## Proposals

These change rules, so they need the owner's decision.

1. **Limit the cost of tiny attacks.** With a 72-hour memory the bots attack all round for little land (one
   warmonger hit the other 55 times for 2,631 acres), and every hit still costs the defender 10% of its units and sends
   an alert. For example, a defender loses no units when the take is below 1% of its land, or a mage can be attacked
   at most N times a day. Best decided with real players' data: real players may not bother with 40-acre attacks.
   Every attack is now in the `actions` table, so after a live round the per-target counts can be read straight off it.
2. **Keep upkeep as it is.** With a 72-hour memory the warmongers no longer run away with the round, and their upkeep peaks
   above their income.

## Not answered by the simulation

Whether to add peasants or population, and whether to have a break or an end-of-round event, are questions about how
the game feels, not about numbers.
