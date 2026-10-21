# Spellbound: design principles

What kind of game Spellbound is, and the principles every rule is weighed against. `game-design.md` has the rules;
this file has the reasons.

**Where each principle comes from** is marked:
- **owner**: the owner said or chose it;
- **design**: it was in the owner's clean-slate design (19 Oct 2026);
- **AI taste**: a taste in an AI playtest asked for it (named, with the review);
- **Claude**: Claude proposed it.

## What kind of game it is

**A short game about land, decided by reading your rivals.** (owner, 21 Oct 2026)

Players grow into a dark map, then compete for it. The deciding skill is guessing what rivals will do with their
secret, simultaneous orders, and doing the thing they didn't guard against. It is a mix of two of the six AI
tastes' homes:
- **Territory:** grow, take land and defend it (the Wargamer's and 4X fan's games: Risk, Kemet, Civilization);
- **Reading rivals:** outguess, bluff and threaten (the Bluffer's and Casual player's games: Diplomacy, Coup,
  Cosmic Encounter).

The owner also said the game is missing elements for reading rivals. Those are listed under *Open*, below.

**The target tastes** are the Wargamer, the 4X fan, the Bluffer and the Casual player. The Euro-gamer and the Combo
player are **outside tastes**. Their ideas are welcome as inspiration, but they are labelled as outsiders' ideas and
weighed less (owner, 21 Oct 2026).

## Principles

1. **One phone, one hand, a few minutes a round.** Text-first pages, a stance is one tap, 8 rounds. (design;
   `CLAUDE.md` UI rules)
2. **Secret, simultaneous orders.** Everyone commits, then everything resolves together. This is where reading
   rivals happens, and all six AI tastes named it the best part. (design; every AI taste, both sessions)
3. **One stance a round.** Every round is a choice between growing, arming, fighting and building. (design)
4. **See the cost before you commit.** Every order shows what it costs and what it will do (the ledger, `check`).
   Hidden information is about rivals, never about the rules. (design; praised by the Euro-gamer, 4X fan and Casual
   player)
5. **Points are banked and never lost.** What you earned early stays yours. (design, `discussions/scoring-and-end.md`)
6. **Everyone stays in the game to the end.** A player's last tile can't be taken, and banked points keep a lead from
   being total. (design, `discussions/player-elimination.md`; game-design.md cited this as principle 12)
7. **The biggest realm pays the most to grow.** Prices that rise with size, and no trait that changes a price,
   income or upkeep, so a lead doesn't snowball. (design; game-design.md cited this as principle 13)
8. **Growing peacefully must stay possible.** A small realm can always afford to attune. (owner, 21 Oct 2026, after
   four of six AI tastes couldn't grow at 1 + 3 per tile)
9. **Several strategies pay off.** No single plan should win most games; the simulator's strategy bots check it,
   with a band of 0.8–1.2× a fair share. Bots can't show what a person enjoys, so AI tastes judge that. (owner,
   21 Oct 2026, `discussions/strategies.md`)
10. **Deterministic and fair.** All randomness comes from the match's seed; a match replays exactly. (design;
    `CLAUDE.md`)

## Open: what reading rivals is missing

The owner said the game is missing elements for reading rivals (21 Oct 2026). These are the ideas so far, with
where each came from. The owner chose the first three to build, in that order: a Rally spell (the commitment), secret traits
with guessing, and fog of war (`discussions/reading-rivals.md`).

| Idea | Who asked | Inspired by |
|---|---|---|
| Traits kept secret until the end, with a reward for guessing a rival's | Bluffer, sessions 1 and 2 ([s2](playtests/2026-10-21-ai-tastes-2/match3-seat1.md)) | Coup |
| A non-binding pact or threat line before orders | Bluffer, sessions 1 and 2 ([s2](playtests/2026-10-21-ai-tastes-2/match3-seat1.md)); Casual player, session 1 (trading mana for a promise) | Diplomacy, Catan |
| A secret commitment of extra power to a battle | Wargamer, session 2 ([review](playtests/2026-10-21-ai-tastes-2/match1-seat2.md)); Bluffer and Combo player (outside), session 1 | Kemet, Blood Rage, Cosmic Encounter |
| A third player can back a side in a battle | Bluffer, session 2 ([review](playtests/2026-10-21-ai-tastes-2/match3-seat1.md)) | Cosmic Encounter's allies |
| A reaction that fires only if a tile is attacked (a trap) | Combo player (outside), sessions 1 and 2 ([s2](playtests/2026-10-21-ai-tastes-2/match2-seat2.md)) | Magic's instants |
| Rivals' units seen only next to your own land (fog), so threats have to be guessed | Claude | Diplomacy's and wargames' fog of war |
