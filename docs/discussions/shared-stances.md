# Shared stances, from Puerto Rico (21 Oct 2026)

**The owner's idea:** lean into Puerto Rico's role selection. Each round someone picks a role, *every* player gets
to do it, and the player who picked it gets a little more (a privilege). Picking a role is then a tactical choice:
what helps you most and your rivals least?

Nothing here is decided. This page weighs ways to bring that into Spellbound against `../design-principles.md`.

## The fit problem

Puerto Rico's roles are picked **in turn and in public**. Spellbound's core is the opposite:
- orders are **secret and simultaneous** (principle 2; all six AI tastes named it the best part);
- a round is **one commit on a phone** (principle 1), and play is often asynchronous.

Two tests sort the variants:
- **Test A:** does a round still need just one commit from each player?
- **Test B:** do orders stay secret until everything resolves?

## The variants

### 1. Follow orders (passes both tests)

You commit your stance in secret, as now. Alongside it you may also commit one **follow order** in a different
stance. It happens only if **some rival chose that stance this round**, and it costs a surcharge (say +2 mana).
- The player who chose the stance pays the normal price. That is the privilege.
- If nobody chose it, the follow order does nothing and costs nothing.

**For it:**
- It is Puerto Rico's tension in simultaneous form: by choosing Attune you also open Attune to everyone who
  guessed you would.
- It adds a real read to every round: "will anyone open Summon this round?" That fits "decided by reading rivals".
- It gives late mana a use, which all four target tastes asked for.
- Bots can use it simply: follow Attune when cheap land is next to them, follow Summon when they are threatened.

**Against:**
- The orders form already has four extras: moves, spell, guess and reinforcements. A fifth is more homework for
  the Casual player, a target taste.
  - One remedy: fold "follow" and "reinforce" into a single "alongside" slot.
- It is the size of a medium feature. `Orders` holds one stance, and about 28 places in the domain read
  `orders.Stance`. A second stance in the orders touches checks, costs and resolution.

### 2. A round leader declares the stance (fails test B, partly)

A leader, rotating each round, publicly names the round's stance. Everyone then commits in secret within that stance,
and the leader gets a privilege (for example a discount).
- It is the closest to Puerto Rico.
- But it removes the one-stance choice that every round turns on. Rivals still guess targets, but no longer what
  kind of move is coming.
- The leader needs a turn of their own before the others commit, so async play waits on one player each round.

### 3. A role draft, in turn (fails both tests)

This is Puerto Rico as written: in seat order, each player picks a role from those left, and everyone does each role
in turn. It is listed so the reason is on record: it needs a turn order and public choices. That replaces the
secret simultaneous round rather than adding to it.

### Also: Puerto Rico's other half

In Puerto Rico, a role nobody picked gathers a doubloon, so it grows more tempting. Here, **a stance nobody chose
last round could cost less this round.** This is cheap to add to any variant, and it is also a catch-up lever, which
the Casual player asked for twice. Not designed yet.

## Timing

This would be a second fix for the same complaint that reinforcements (#89) just addressed: mana piles up, and there
is one action a round. The fifth AI-taste session hasn't measured reinforcements yet. Shipping both without that
measurement would make it hard to tell which helped.

## Decided so far (21 Oct 2026, the owner)

- Design follow orders in detail now.
- Measure reinforcements first, in the fifth AI-taste session.
- Build follow orders after that, knowing what reinforcements did.

## Removed (21 Oct 2026)

Follow orders were built (#91) and taken out again the same day, the owner's choice after the sixth AI-taste session
(`../playtests/ai-feedback.md`). The AI players committed 13 follow orders and 4 happened. After round 2 nearly
everyone attacks, so there was little to follow except the scripted bot, and the extra form section confused the
Casual player. Follow orders need rivals to choose varied stances, which this game doesn't produce after its opening.
The design below stays for a future version that does (for example, one where Attune and Build are chosen often).

## Follow orders in detail (built 21 Oct 2026, then removed)

**The rule.** Alongside your stance you may commit one **follow order**:
- It is an Attune, a Summon or a Build, in a stance other than your own.
- It **happens only if some rival's stance this round is the same** (and is aimed at something).
- If it happens, it costs the usual price **plus 2 mana**. If it doesn't, it costs nothing and nothing happens.
- No following Attack. A conditional attack would be a second army order every round, too strong and too much to
  read.

**The privilege of the one who chose.**
- They pay the normal price.
- **They win a clash with a follower:** if a chooser and a follower attune the same tile, the chooser gets it.
  Normally a clash gives the tile to nobody. Between followers, or between choosers, nobody gets it, as now.

**When it resolves:** with its stance's step, so a follow Attune with the Attunes, a follow Summon with the Summons
and a follow Build with the Builds. Spells come after, so a follow Summon can be warded or ride a Leyline.

**What it costs to check:** when you commit, the check counts the follow order as if it will happen. Your stance, your
spell and the follow order must fit your mana together, so nothing fails later.

**The report:**
- "Seat 3 follows Attune: attunes the Wild tile at (1, 0) for 6 mana."
- A follow order that doesn't happen is reported only to its seat: "Nobody chose Summon; your follow order did
  nothing."
- The fog applies as for any order.

**One extra, not two.** The orders form already has four extras. Follow orders and reinforcements share one slot,
"Alongside": with an Attack you may reinforce, or follow; with any other stance, follow. That keeps the form at four
extras.

**Bots:**
- follow Attune when cheap land is next to them and their own stance isn't Attune;
- follow Summon when a rival's stack threatens them;
- follow Build from round 3 with mana to spare.
- Their follow orders go through the same `Check`, so the strategy bots measure it with `--plans` like everything
  else.

**What it touches:**
- `Orders` (a `Follow` part);
- `Check` (a follow problem and the combined cost);
- `Resolve` (which stances were chosen, then the follow orders in each step, and the chooser's clash privilege);
- bots, the orders form, the Table's JSON and rules text, tests, docs, and a playtest step.

Medium.

**Numbers to measure first** (with `--set`):
- the surcharge: +2, or +1;
- whether Build is followable.
