# Do different strategies pay off? (21 Oct 2026)

The owner asked to make sure different strategies pay off. Two caveats on what follows:
- The AI sessions (`../playtests/`) showed players with different plans, but five matches can't tell which plan pays.
- The trait numbers measure traits, not plans.

So the simulator gained **strategy bots** (`Plan` in `Bot.cs`):

```
dotnet run --project tools/Spellbound.Simulator -- --players 3 --games 2000 --plans
```

## The plans

| Plan | What it puts first | Spends |
|---|---|---|
| Balanced | today's bot: attack what it can beat, else attune or summon (alternating rounds), build and cast with spare mana | the usual |
| Warlord | attack; otherwise summon an Imp army (¾ of its mana) until it holds a stack of 8 power, then attune too | spells with 3 to spare; Scorch first |
| Builder | upgrades from round 2 (a Shrine, or a Tower on a threatened tile), then attune; fights only monsters | spells only with 10 to spare |
| Spellcaster | attack what it can beat, then a Sanctum, then attune | a spell every round it can afford |
| Expander | attune every round it can | spells only with 10 to spare |
| Hoarder | passes every auction; attunes only while it's cheap (4 or less); clears monsters it can beat; otherwise holds | Ward only, with 12 to spare |

Every plan defends first when a rival's stack next to it outweighs its own: it summons just enough Imps there. Every
plan gives only orders that check.

The setup:
- With 5 players, each match has the five specialists, one each.
- With 3 players, each match draws 3 of the 6 plans, Balanced included.
- Plans are dealt to seats from their own seeded draw, so seat order doesn't decide.
- Traits are drafted at random, as usual.

**What "pays off" means, set before the numbers:** a win rate between 0.8× and 1.2× of an even share. That is 27–40%
with 3 players and 16–24% with 5. A plan below 0.7× (under 23% or 14%) doesn't pay.

## Measured (2,000 games each)

| Plan | 3 players: win rate | 5 players: win rate | Score | Land | Mana | Conquest | Upgrades | Saved mana | Mana left |
|---|---|---|---|---|---|---|---|---|---|
| Balanced | 28% | – | 18.7 | 9.8 | 4.4 | 0.5 | 2.4 | 0.5 | 9 |
| Warlord | 27% | **14%** | 17.0 | 9.3 | 4.0 | 1.1 | 0.3 | 1.1 | 14 |
| Builder | 39% | 19% | 20.4 | 10.0 | 5.1 | 0.0 | 2.7 | 1.6 | 21 |
| Spellcaster | 23% | 19% | 18.0 | 9.2 | 3.9 | 0.4 | 2.5 | 1.0 | 14 |
| Expander | **70%** | **42%** | 23.2 | 13.8 | 5.9 | 0.1 | 0.0 | 2.2 | 27 |
| Hoarder | **12%** | **5%** | 17.5 | 9.9 | 3.9 | 0.0 | 0.0 | 3.1 | 35 |

The points by source are from the 3-player run.

- **Land is king.** The Expander attunes every round. It earns 4 more land points than anyone (13.8 against about
  9.5), plus the mana that land produces, and it wins 70% of 3-player games. Attune's rising price doesn't stop it.
- **Fighting doesn't pay.** The Warlord earns only about 1 conquest point a match. Attacks cost the stance, the
  units and the mana, and defenders summon on the attacked tile. The Wargamer said the same in the AI review: "the
  scoring pays the player who doesn't fight".
- **Saving alone doesn't pay.** The Hoarder ends with 35 mana, worth 3 points. That doesn't make up for the land it
  didn't take.
- **Builder and Spellcaster are inside the band, or close to it.** Upgrades (about 2.5 points) are worth their cost.

## Options for the owner

1. **Expander too strong:** make land dearer as it grows (Attune 1 + 2 per tile held), or score land less (1 point per
   4 tile-rounds).
2. **Warlord too weak:**
   - 2 points per conquest;
   - plunder: take some of the loser's mana with the tile;
   - a point for fighting, win or lose (the Wargamer's Blood Rage wish).
3. **Hoarder too weak:** 1 point per 5 unspent mana; or accept that pure saving is a weak plan.

Each option gets re-measured with `--plans` before it's kept.
