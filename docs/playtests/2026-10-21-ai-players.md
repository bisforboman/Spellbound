# AI players, first session (21 Oct 2026)

Five 3-player matches. In each, two AI players with a personality ([personalities](../../tools/Spellbound.Table/personalities.md))
played one seat each, against a scripted bot in the third seat, through `tools/Spellbound.Table`. They learned the
game only from the tool's `rules` and `view`, never the design document. Each wrote a note at the end (in
[2026-10-21-ai-players/](2026-10-21-ai-players/), with every match's log).

This is a playtest, not a balance measurement: five matches are noise. Read the findings, not the scores.

| Match (seed) | Seat 1 | Seat 2 | Seat 3 (bot) |
|---|---|---|---|
| 1 (101) | Warlord, Weaver: **18** | Hoarder, Wanderer: 17 | 14 |
| 2 (102) | Spellcaster, Warden: 22 | Rules-lawyer, Hearthkeeper: **23** | 16 |
| 3 (103) | Trait-chaser, Beastbane: 22 | Warlord, Hexer: **32** | Weaver: 9 |
| 4 (104) | Hoarder, Hearthkeeper: **24** | Spellcaster, Beastbane: 21 | 15 |
| 5 (105) | Rules-lawyer, Spellweaver: 13 | Trait-chaser, Warbrand: **36** | 19 |

The AI players beat the scripted bot in every match. Only 3 of about 110 actions were refused, and all three refusals
were about a rule, not the action format.

## What they found

### Scorch is the strongest thing in the game (4 of the 10 notes)

- **It wins fights before they start.** Spells resolve after summons and before attacks, so a Scorch on the rival
  stack next to you kills an attack before it lands. One player stopped three attacks this way. In match 5, Seat 1's
  every attack ended "no units left to send". At 2 mana per power, Scorch beats summoning defenders, because you
  don't have to guess which tile the rival will hit.
- **It costs the full price for nothing** when the units have moved away first (moves resolve before spells), or when
  another seat's Scorch burned them already. This happened several times, for 4 to 10 mana.
- **A partial burn on a Golem is wasted but paid for.** Scorch 9 on 7 Imps and a Golem burns only the 7 Imps, because
  losses stop before a unit that would go past the amount. The check said "burn 9 power"; its "leaving 1 Golem" was
  the only clue.
- **It can't be aimed at an empty tile**, although a rival may summon there before spells resolve.

The scripted bots almost never cast Scorch (0.0 to 0.4 casts a game in the simulator). Players do. The bots were
hiding the strongest spell, not the rules.

### The check's preview ignores your own spell

An attack's check said "you lose everything you sent" or "keeping 3 Imps" without counting the Scorch cast in the
same orders. The attack then won (3 notes). This breaks the rule that every action shows its projected result.

### Spells are checked against the board before the round's attunes and summons

The rules say attunes and summons resolve before spells. Even so:

- A Ward on a tile attuned the same round is refused.
- A Leyline of units summoned the same round is refused ("There aren't 2 Imps at (-1, 0)").

Two players called this inconsistent.

### The opening: passing and going all-in cost almost nothing

- A seat that passes every auction still gets 3 free tiles and keeps all 9 mana.
- A seat that bids all 9 and wins still gets free leftovers in the next auctions.
- A tie at the top wins nobody the tile, but the tile then joins the leftovers, so a tied bidder can get it free (3
  notes).

### Mana piles up with nothing to buy (3 notes)

Seats ended with 34, 47 and 67 mana unspent. Attacking takes the stance, so a warring seat can't also summon, and
unspent mana scores nothing.

### Smaller points

- **Imps over Golems:** Imps (1 mana per power, no upkeep) beat Golems "almost everywhere" in a short game (3 notes).
  The match-3 Warlord's Golems disagree.
- **Beastbane can't always reach its cap:** match 3's map had only 2 monster tiles, so 3 points were impossible.
- **Wanderer fights conquest:** taking a rival's tile next to yours can join your groups.
- **Traits are public from round 1:** the rules text said goals are "hidden until then" (the end), and players read
  that as the trait itself. The tool's wording is fixed in this change: traits are revealed when the rounds begin,
  and only the goal's points wait for the end.
- **Telegraphed attacks:** a stack next to a rival shows where you'll strike, and the rival summons defenders that same
  round.
- **Ward rarely matters:** players found it weak on guesses, since the rival picks which of three tiles to hit. A Ward
  on an empty tile still counts as 3 defenders.
- **Mixed actions:** `{"trait": …, "bid": …}` in one action dropped the bid silently. The tool now refuses a mixed
  action.

## Worth deciding

The owner's calls. The first three go back into the game design:

1. **Scorch:** keep it, or tame it? Options: refund what doesn't burn; raise the price to 3 mana per power; or only
   burn defenders, not attackers.
2. **Spells after attunes and summons:** check them against the board as it will be (Ward a tile you attune, Leyline
   units you summon), or change the stated order.
3. **The previews:** count your own spell in the attack's check (a fix in the check, not a rule change).
4. **The opening's ties and passing:** leave them as they are, or close the gap? For example, a tied tile could be
   taken out of the leftovers.
