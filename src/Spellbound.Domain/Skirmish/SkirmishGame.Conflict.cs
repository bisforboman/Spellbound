namespace Spellbound.Domain.Skirmish;

/// <summary>Spells, hexes, attacks and players who stop playing: the parts of a round where mages meet.</summary>
public sealed partial class SkirmishGame
{
    /// <summary>
    /// Chance a hex lands on the target: the base chance, moved by the school ring, then scaled by the target's ward.
    /// Shown before the player commits.
    /// </summary>
    public double HexChance(int caster, int target) =>
        Math.Clamp(Rules.HexBaseChance + (CounterSign(caster, target) * Rules.HexCounterBonus), 0, 1)
        * Seats[target].Multiplier(EffectKind.HexWard);

    /// <summary>
    /// A region's defense before luck: the holder's army plus the region's own guards and buildings, stronger on Terrain 4 and halved
    /// while the holder is away. Shown before the player commits.
    /// </summary>
    public double RegionDefense(int regionId)
    {
        var region = Board[regionId];
        if (region.Owner is not { } owner)
        {
            return 0;
        }

        return (Seats[owner].DefensePower + Rules.RegionGuard + (region.Buildings * Rules.DefensePerBuilding))
               * (region.Terrain == SkirmishTerrain.T4 ? Rules.Terrain4Defense : 1)
               * (Seats[owner].IsAway(Rules) ? Rules.AwayDefense : 1);
    }

    /// <summary>An attacker's power against a seat before luck: its army, moved by the school ring.</summary>
    public double AttackStrength(int attacker, int defender) =>
        Seats[attacker].AttackPower * (1 + (CounterSign(attacker, defender) * Rules.CounterBonus));

    /// <summary>+1 if the first seat's school is strong against the second's, -1 if weak, 0 otherwise.</summary>
    public int CounterSign(int seat, int other) =>
        Seats[seat].School.StrongAgainst() == Seats[other].School ? 1
        : Seats[seat].School.WeakAgainst() == Seats[other].School ? -1
        : 0;

    /// <summary>Who readied up: a seat that missed enough rounds in a row is away, then out.</summary>
    private void PlayAttendance(IReadOnlySet<int>? ready, List<string> shared)
    {
        foreach (var seat in Seats.Where(s => !s.Out))
        {
            seat.MissedInARow = ready is null || ready.Contains(seat.Index) ? 0 : seat.MissedInARow + 1;
            if (seat.MissedInARow >= Rules.OutAfterMissed)
            {
                seat.Out = true;
                foreach (var region in RegionsOf(seat.Index).ToList())
                {
                    region.Owner = null;
                }

                shared.Add($"{seat.Name} hasn't played for {seat.MissedInARow} rounds and is out: their regions are free to claim.");
            }
            else if (seat.MissedInARow == Rules.AwayAfterMissed)
            {
                shared.Add($"{seat.Name} is away: their regions defend at {Percent(Rules.AwayDefense)} until they play again.");
            }
        }
    }

    /// <summary>Self-spells first (so this round's wards count), then hexes, seat by seat.</summary>
    private void PlayCasts(List<List<SkirmishOrder>> accepted, List<string> shared, List<List<string>> mine, IRandomSource random)
    {
        foreach (var seat in Seats)
        {
            foreach (var cast in accepted[seat.Index].OfType<CastOrder>())
            {
                if (Problem(seat.Index, cast, NoClaims) is { } problem)
                {
                    mine[seat.Index].Add($"{Describe(cast)}: not done. {problem}");
                    continue;
                }

                var spell = SpellBook.Find(cast.SpellId)!;
                seat.Mana -= SkirmishSpells.ManaCost(spell, Rules);
                if (spell.Effect == EffectKind.MeditateYield)
                {
                    seat.ActionBonus += 1;
                }
                else
                {
                    seat.Effects.RemoveAll(e => e.SpellId == spell.Id);
                    seat.Effects.Add(new SkirmishEffect(spell.Id, Rules.SpellRounds));
                }

                mine[seat.Index].Add($"You cast {spell.Name}: {SkirmishSpells.Describe(spell, Rules)}");
            }
        }

        foreach (var seat in Seats)
        {
            foreach (var order in accepted[seat.Index].OfType<HexOrder>())
            {
                if (Problem(seat.Index, order, NoClaims) is { } problem)
                {
                    mine[seat.Index].Add($"{Describe(order)}: not done. {problem}");
                    continue;
                }

                var hex = SpellBook.FindHex(order.HexId)!;
                var target = Seats[order.TargetSeat];
                seat.Mana -= Rules.HexMana;
                var chance = HexChance(seat.Index, target.Index);
                if (random.NextDouble() >= chance)
                {
                    mine[seat.Index].Add($"You cast {hex.Name} at {target.Name} ({Percent(chance)} to land), but it was turned aside.");
                    mine[target.Index].Add($"{seat.Name} cast {hex.Name} at you, but it was turned aside.");
                    continue;
                }

                var effect = HexEffectOn(hex, seat, target);
                mine[seat.Index].Add($"You cast {hex.Name} at {target.Name}: {effect}.");
                mine[target.Index].Add($"{seat.Name} cast {hex.Name} at you: {effect}.");
                shared.Add($"{seat.Name} cast {hex.Name} at {target.Name}.");
            }
        }
    }

    private string HexEffectOn(HexDefinition hex, SkirmishSeat caster, SkirmishSeat target)
    {
        switch (hex.Effect)
        {
            case HexEffect.KillUnits:
                return $"{N(target.LoseUnits(Rules.HexKillPercent, roundUp: true))} units died";
            case HexEffect.DestroyGold:
                var gold = target.Gold * Rules.HexGoldPercent / 100;
                target.Gold -= gold;
                return $"{N(gold)} gold was destroyed";
            case HexEffect.StealMana:
                var mana = target.Mana * Rules.HexManaPercent / 100;
                target.Mana -= mana;
                caster.Mana += mana;
                return $"{N(mana)} mana was taken";
            case HexEffect.BurnBuildings:
                var region = RegionsOf(target.Index).OrderByDescending(r => r.Buildings).ThenBy(r => r.Id).FirstOrDefault();
                var burned = 0;
                for (; region is not null && burned < Rules.HexBuildingsDestroyed && region.Buildings > 0; burned++)
                {
                    // The kind they have more of goes first.
                    if (region.Towns >= region.Nodes)
                    {
                        region.Towns--;
                    }
                    else
                    {
                        region.Nodes--;
                    }
                }

                return burned == 0 ? "they had nothing built" : $"{burned} building{(burned == 1 ? "" : "s")} in {region!.Name} destroyed";
            case HexEffect.DrainTurns:
                target.ActionBonus -= Rules.HexActionsTaken;
                return $"{Rules.HexActionsTaken} action{(Rules.HexActionsTaken == 1 ? "" : "s")} fewer next round";
            default:
                throw new ArgumentOutOfRangeException(nameof(hex), hex.Effect, null);
        }
    }

    /// <summary>
    /// Attacks, all at once, each against the region's defense as the round began. A region several mages break goes to
    /// the strongest attack. Losses are taken after every battle has been fought. Rolls are drawn by region, then seat.
    /// </summary>
    private void PlayAttacks(List<List<SkirmishOrder>> accepted, List<string> shared, List<List<string>> mine, IRandomSource random)
    {
        var battles = new List<(int Seat, int RegionId, int Defender, double Attack, double Defense)>();
        foreach (var seat in Seats)
        {
            foreach (var order in accepted[seat.Index].OfType<AttackOrder>())
            {
                if (Problem(seat.Index, order, NoClaims) is { } problem)
                {
                    mine[seat.Index].Add($"{Describe(order)}: not done. {problem}");
                    continue;
                }

                battles.Add((seat.Index, order.RegionId, Board[order.RegionId].Owner!.Value, 0, 0));
            }
        }

        battles = [.. battles.OrderBy(b => b.RegionId).ThenBy(b => b.Seat)
            .Select(b => b with
            {
                Attack = AttackStrength(b.Seat, b.Defender) * Roll(random),
                Defense = RegionDefense(b.RegionId) * Roll(random),
            })];

        foreach (var battle in battles)
        {
            var (attacker, defender, region) = (Seats[battle.Seat], Seats[battle.Defender], Board[battle.RegionId]);
            var won = battle.Attack > battle.Defense;
            var ours = attacker.LoseUnits(won ? Rules.AttackerLossPercentOnWin : Rules.AttackerLossPercentOnLoss, roundUp: false);
            var theirs = defender.LoseUnits(won ? Rules.DefenderLossPercentOnLoss : Rules.DefenderLossPercentOnWin, roundUp: false);
            var power = $"{N((int)battle.Attack)} against {N((int)battle.Defense)}";
            mine[attacker.Index].Add(won
                ? $"You broke through at {region.Name} ({power}). You lost {N(ours)} units; they lost {N(theirs)}."
                : $"Your attack on {region.Name} was held ({power}). You lost {N(ours)} units; they lost {N(theirs)}.");
            mine[defender.Index].Add(won
                ? $"{attacker.Name} broke through at {region.Name}. You lost {N(theirs)} units; they lost {N(ours)}."
                : $"{attacker.Name} attacked {region.Name}, but you held. You lost {N(theirs)} units; they lost {N(ours)}.");
        }

        foreach (var group in battles.Where(b => b.Attack > b.Defense).GroupBy(b => b.RegionId))
        {
            var taker = group.OrderByDescending(b => b.Attack).ThenBy(b => b.Seat).First();
            var region = Board[group.Key];
            region.Owner = taker.Seat;
            Seats[taker.Seat].BattlesWon++;
            var destroyed = region.Buildings * Rules.CaptureDestroysPercent / 100;
            var (nodes, towns) = (Math.Min(region.Nodes, destroyed / 2), 0);
            towns = Math.Min(region.Towns, destroyed - nodes);
            nodes = Math.Min(region.Nodes, destroyed - towns);
            (region.Nodes, region.Towns) = (region.Nodes - nodes, region.Towns - towns);
            shared.Add($"{Seats[taker.Seat].Name} took {region.Name} from {Seats[taker.Defender].Name}"
                       + (nodes + towns > 0 ? $"; {nodes + towns} of its buildings were destroyed in the fighting." : "."));
            foreach (var other in group.Where(b => b.Seat != taker.Seat))
            {
                mine[other.Seat].Add($"{Seats[taker.Seat].Name}'s attack on {region.Name} was stronger: they took it.");
            }
        }

        foreach (var held in battles.Where(b => b.Attack <= b.Defense))
        {
            shared.Add($"{Seats[held.Defender].Name} held {Board[held.RegionId].Name} against {Seats[held.Seat].Name}.");
        }
    }

    /// <summary>Running spells count down at the end of each round.</summary>
    private void TickEffects()
    {
        foreach (var seat in Seats)
        {
            for (var i = seat.Effects.Count - 1; i >= 0; i--)
            {
                var left = seat.Effects[i].RoundsLeft - 1;
                if (left <= 0)
                {
                    seat.Effects.RemoveAt(i);
                }
                else
                {
                    seat.Effects[i] = seat.Effects[i] with { RoundsLeft = left };
                }
            }
        }
    }

    private double Roll(IRandomSource random) => 1 + (((random.NextDouble() * 2) - 1) * Rules.CombatRollSpread);
}
