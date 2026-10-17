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
- **Turtle:** the same, plus one militia per acre and an Arcane Barrier.
- **Warmonger:** the same, plus half its spare gold into knights. It attacks the biggest target in range it is
  certain to beat, as often as its turns allow.
- **Idle:** signs up and never plays.

**Limits:**
- Real players won't play this perfectly or this greedily.
- The warmonger sees its targets' true defense, which real players can't, so these results are the worst case for
  defenders.
- Hexes, Meditate and most spells aren't used.

The conclusions hold across seeds 1–3; the exact numbers move a few percent.

## What the numbers say

### 1. Attacking dominates, and the same target can be hit again and again

- On the mixed server the busy warmonger ends with **10,526 acres**, and the builders and turtle with about 3,300–3,500.
- On a peaceful server those same three builders reach about 5,800–6,800 acres, so the attackers halve everyone
  else's growth.
- One warmonger hit the other **29 times** in a round, up to **5 times in one day**, taking 12,771 acres in total.
- The once-a-day warmonger hit each of the three peaceful players 13–17 times.
- Every attack takes 10% of the target's land, whatever happened to it before. Nothing slows repeated attacks; that
  is the open question "should repeated attacks take less land?", and the answer is yes.

### 2. Protection ends in the first session

Protection lasts until a mage has spent 120 turns. A new player starts with 100 turns and has 180 by their first
check-in, so an active player is unprotected by the end of their **first session**. The warmonger attacked builders
on day 1. Protection only lasts for players who don't play: the idle account never spent a turn and was never
attacked.

### 3. Upkeep isn't too harsh, because stolen land pays for the army

Both warmongers spend **95–100%** of their income on upkeep and still win the round. The land they take keeps
raising their income, so upkeep doesn't limit an attacker. That answers "is upkeep too harsh?": no, and lowering it
would make attacking stronger still. The turtle's militia costs about 11% of its income and still didn't stop the
knights. One militia per acre isn't enough defense against a dedicated attacker, which is reasonable as long as
repeated attacks are limited (point 1).

### 4. Checking in once a day loses about a sixth of your turns

A day makes 240 turns, but you can hold at most 200. A once-a-day player loses **1,180 turns (17%)** over the round,
about 40 a day. In the peaceful run that left them 14% behind on land (5,825 against 6,785 acres). Three check-ins a
day lose nothing. That's the open question "should there be a stored-turns bank?"

### 5. The economy grows steadily

Land grows quickly at first (250 to about 900 acres on day 1), then by about 210 acres a day from the second week, as
exploring gets less productive. The round doesn't stall and nobody runs away by economy alone: the builders and the
turtle end within 15% of each other.

### Not answered by the simulation

Whether to add peasants or population, and whether to have a break or an end-of-round event, are questions about how
the game feels, not about numbers.

## Proposals

These change rules, so they need the owner's decision. Each is a `GameRules` number or a small rule with tests.

1. **Diminishing returns on repeated attacks.** Every successful attack on a mage in the last 24 hours, by anyone,
   halves the land the next one takes: 10%, then 5%, 2.5%, and so on. A target recovers a day after the last hit. This
   stops farming without stopping war.
2. **Protection by time as well as turns.** For example, protected for 72 hours *and* until 120 turns are spent, so
   a new player gets three days to build before anyone can attack.
3. **Raise the turn cap to 240**, one day of turns, so checking in once a day loses nothing. This is simpler than a
   stored-turns bank, and enough to make "five minutes a few times a day" truly optional.
4. **Keep upkeep as it is** for now. Revisit after 1 and 2, which take away the stolen land that pays for it.

After a change, rerun the simulation and compare it with the numbers above.
