# Reading rivals: three ideas to design (21 Oct 2026)

The owner chose the game's identity: **a short game about land, decided by reading your rivals**
(`../design-principles.md`). The owner said the game is missing elements for the "reading rivals" half, and picked
three of the ideas to design:
- a secret battle commitment;
- traits kept secret until the end;
- fog of war.

Nothing here is decided. Each section gives who asked for the idea, a proposed rule, the open choices (each with
Claude's recommendation), and what the change touches.

One caution covers all three. The Casual player is a target taste and asked for *less* homework. Three kinds of
hidden information at once pushes against principle 1 ("one phone, one hand"). The proposal is to build them one at
a time, in the order below, so the owner can stop after any of them.

## 1. A secret battle commitment

**Who asked:**
- the Wargamer, session 2: "battle cards or a secret commitment of bonus power, a chance to outguess the other
  player, beyond comparing totals";
- the Bluffer, session 1: face-down reinforcements;
- the Combo player (outside), session 1.

**What already exists.** Ward is already the defender's half. It is cast in secret alongside the stance, revealed
when the round resolves, and gives a tile 3 more power for 3 mana. What's missing is **the attacker's side**, and
maybe **a variable amount**.

**Proposed rule: Rally**, Ward's mirror for the attacker:
- A spell that adds power to your own attack this round: **+3 power for 3 mana**, aimed at the tile you attack.
- Revealed only when the round resolves, like every order.
- Paid whether or not a battle happens. Otherwise it would be free insurance that beats summoning Imps.
- If a Scorch leaves your attack with no units to send, is Rally refunded? Proposed: no. A bluff that misses costs,
  as Ward does.

**Open choice:**
- (a) **Add Rally and leave Ward alone** (recommended). It is one new spell, and nothing players know changes.
- (b) **One "commit" mechanic for both sides, any amount**: both attacker and defender put any mana on a battle, at
  1 power per 2 mana, and Ward goes away. It is a bigger change, but closer to Cosmic Encounter's face-down cards.

**What it touches:**
- domain: a new spell and the battle sum;
- bots;
- the orders form's spell list;
- the Table rules text;
- tests and `--plans`.

Small.

## 2. Traits secret until the end

**Who asked:** the Bluffer, sessions 1 and 2: "keep traits secret until the end, so players guess, like Coup's hidden
roles".

**This reverses a decision in the design.** Traits are now revealed when round 1 starts (walkthrough step 3 and
*Traits* in `game-design.md`). `discussions/traits.md` built "the opening tells" on that: the opening is the only
time the secret matters.

**Proposed rule:**
- A trait stays hidden until the match ends. Its goal points are already hidden until then.
- **One guess per rival, once per match**, made alongside any round's orders (a guess can't be changed).
- **1 point per correct guess**, scored at the end, capped at the trait-goal cap (3). Guessing never outweighs
  playing.
- The opening tells now matter all game: everything a rival does is a clue.

**Open choice:**
- (a) **Guess any round, once per rival** (recommended): early guesses risk being wrong, late ones are safer but you
  may forget.
- (b) **Guess only in round 8**, alongside the last orders: simpler, but there's no tension before then.

**What it touches:**
- domain: a guess order, scoring, and keeping the trait hidden;
- the match page, which shows "Rivals' traits: …" today and must stop;
- the Table `view`, which shows rivals' traits today;
- a guess form on the page;
- bots must guess, or the simulator can't measure it. A bot can guess from what its rivals bid on, which is what
  `Bot.Suits` already describes;
- tests, the playtest and docs.

Medium.

## 3. Fog of war

**Who suggested:** Claude. No taste asked for it directly, but the Bluffer's "reading rivals" and the Wargamer's
"no army I had to outplan" point at it.

**Proposed rule:**
- You see a rival's units only on tiles next to your own land.
- Everything else stays visible: who owns every tile, every tile's yield and upgrades, and the scores.
- Units only ever stand on their owner's tiles, so "next to your land" is the whole rule.

**The real work is the round report.** Today the report is one list of lines for everyone ("Seat 2 summons 4 Imps
at (0, -2)"). The page's "Everything that happened" shows it all. Fog needs a report for each seat, used by:
- the page;
- the Table's `view` and `act`;
- the Table's log, which the AI players read.

Otherwise the next AI session judges a game that leaks.

**Open choices:**
- **Hide rivals' mana as well?** The Casual player was surprised by a rival "sitting on 64 mana". But the tie-break
  uses mana, and hiding it makes a big mana stack a threat to guess. Recommended: hide it during the match and show
  it at the end.
- **Scry**, a stance to peek at hidden things, is already listed in `game-design.md` as a possible stance. Fog is
  what would make it worth having. Proposed: note it for later, don't build it now.

**What it touches:**
- domain: a per-seat view and report;
- the web page;
- the Table tool;
- bots: they must read only what they can see; check that none reads units far away;
- tests, the playtest and docs.

Larger.

## Order

Proposed, one PR each:
1. Rally (small);
2. secret traits (medium);
3. fog (larger).

The third AI-taste session comes after all three, with the target tastes weighted most.

## Decided (21 Oct 2026, the owner)

1. **Rally**, the attacker's Ward: +3 power to your own attack this round for 3 mana, paid even if the attack never
   happens. Ward stays as it is. *Built at +2 power (owner's choice): at +3 the Warlord strategy bot won 48% of
   3-player games and 38% of 5-player games (34% and 23% without Rally); at +2, 44% and 32%. Bots defend worse than
   people, who can Ward on a hunch, so the next AI session judges it.*
2. **Traits stay secret until the end.** Each player may guess each rival's trait once, alongside any round's orders.
   A correct guess is worth 1 point at the end; guesses are capped at 3 points.
3. **Fog of war:** rivals' units are seen only on tiles next to your land. Rivals' mana is hidden until the end.
4. Build them in that order, one PR each, and ask the owner after each one whether to go on.
