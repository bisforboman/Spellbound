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
