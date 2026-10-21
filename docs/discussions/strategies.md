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
| Spellcaster | attack what it can beat, then a Sanctum (from round 2), then attune | a spell every round it can afford |
| Expander | attune every round it can | spells only with 10 to spare |
| Hoarder | passes every auction; attunes only while it's cheap (4 or less); clears monsters it can beat; otherwise holds | Ward only, with 12 to spare |
| Economist (replaced the Hoarder) | a Shrine on its best open tile from round 2, then attunes rich land (yield 2 or more) first, clears monsters it can beat, else attunes any land | Ward or Blight only, with 10 to spare |

Every plan defends first when a rival's stack next to it outweighs its own: it summons just enough Imps there. Every
plan gives only orders that check. In the last round every plan (Balanced too) attacks if it can, else buys an
upgrade, because mana left at the end scores nothing.

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

## Decided (21 Oct 2026)

The owner chose:
- **Expander:** a dearer Attune, 1 mana + 2 per tile held (with 3 tiles, 7 instead of 4).
- **Warlord:** 2 points per conquest (a rival tile taken or a monster tile cleared).
- **Hoarder:** "Maybe hoarder is a bad idea. Better to focus on how much mana you produce per turn instead?" So the
  end-of-match points for unspent mana are gone. Mana produced scores 1 per 10 (was 12). The Hoarder plan is replaced
  by an **Economist** that goes for income.

## Re-measured after the decisions (2,000 games each)

| Plan | 3 players: win rate | 5 players: win rate | Score | Land | Mana | Conquest | Upgrades | Mana left |
|---|---|---|---|---|---|---|---|---|
| Balanced | 33% | – | 19.5 | 9.8 | 5.3 | 1.1 | 2.2 | 9 |
| Warlord | 34% | 26% | 18.7 | 9.6 | 5.0 | 2.7 | 0.4 | 15 |
| Builder | 31% | **14%** | 19.3 | 9.8 | 6.0 | 0.0 | 2.6 | 17 |
| Spellcaster | **18%** | 21% | 17.5 | 8.6 | 4.4 | 1.0 | 2.4 | 11 |
| Expander | **56%** | 28% | 21.2 | 13.1 | 6.7 | 0.3 | 0.0 | 17 |
| Economist | 27% | **11%** | 19.0 | 9.5 | 6.0 | 0.0 | 2.6 | 18 |

Against the band (27–40% with 3 players, 16–24% with 5):
- **The Warlord now pays.** It earns about 2.7 conquest points a match and is in the band or above it with 5 players.
- **The Expander is still the strongest, but far less so.** It won 70% and 42% before; now it wins 56% and 28%.
- **The Economist pays with 3 players but not with 5.** It plays much like the Builder; both end with 17–18 mana
  unspent. That is partly the bots: they leave mana they could spend.
- **The Spellcaster slipped with 3 players** (23% to 18%). Spells cost the same, but everything else got dearer or
  worth more.

With 3 players, four of six plans are now in the band (three were before), and none is below 0.7× except the
Spellcaster. With 5 players the spread is narrower (11–28% instead of 5–42%), but only the Spellcaster is inside the
band: the Warlord and the Expander sit just above it, the Builder and the Economist below.

## Second pass (21 Oct 2026)

First, the bots were fixed where they played badly:
- The Spellcaster built its Sanctum in round 1, which starved its land; it now waits for round 2, as the others do.
- The Economist skipped land when nothing rich was next to it; it now falls back to any land.
- Every bot now spends in the last round: an attack, else an upgrade.

That brought the Spellcaster from 18% to 25%, but the Expander still won 55% of 3-player games. The simulator gained
`--set Rule=value` to measure a rule change before making it. Of the two options, scoring land at 1 per 4 tile-rounds
only brought the Expander to 50% and cut every score by about 2.5. **Attune at 1 + 3 per tile held** brought it to
41%, with land points almost unchanged (9.6 to 9.4 a seat). The owner chose that.

| Plan | 3 players: win rate | 5 players: win rate | Score | Land | Mana | Conquest | Upgrades | Mana left |
|---|---|---|---|---|---|---|---|---|
| Balanced | 28% | – | 18.8 | 9.2 | 5.1 | 1.6 | 2.0 | 8 |
| Warlord | 37% | 25% | 19.8 | 9.5 | 5.1 | 3.4 | 0.8 | 16 |
| Builder | 33% | 15% | 19.2 | 9.2 | 5.6 | 1.1 | 2.4 | 18 |
| Spellcaster | 28% | 22% | 18.5 | 9.3 | 4.9 | 1.7 | 1.5 | 14 |
| Expander | 41% | 24% | 19.9 | 11.3 | 5.9 | 1.4 | 0.3 | 17 |
| Economist | 33% | 14% | 19.2 | 9.2 | 5.8 | 1.1 | 2.3 | 19 |

With 3 players every plan is now between 0.85× and 1.25× a fair share. With 5 players the spread is 14–25%:
- the Spellcaster and the Expander are in the band;
- the Warlord is just above it;
- the Builder and the Economist are just below it, but not under 0.7×.

Still open:
- With 5 players, the plans that stay home (Builder, Economist) do a little worse. A crowded map leaves less to build
  on and more neighbours to fight.
- Every plan but Balanced ends with 14–19 mana. Part of that is the last round's income, which arrives after the last
  orders.
