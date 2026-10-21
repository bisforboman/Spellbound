namespace Spellbound.Domain;

/// <summary>Spells, cast alongside the stance (docs/game-design.md, Spells). They resolve after moves, Attunes and
/// Summons, in seat order, and before attacks; Blight lands in the next round's income.</summary>
public sealed partial class Match
{
    // This round's Wards (extra defence by tile) and Blights (tiles that yield nothing in the next income). Both are
    // used up within the Resolve that makes them, so they are never saved.
    private readonly Dictionary<Hex, int> wards = [];
    private readonly HashSet<Hex> blights = [];

    // This round's Rallies: extra attack power by seat.
    private readonly Dictionary<int, int> rallies = [];

    // The tiles blighted last round (they yielded nothing this round's income), which can't be blighted again now.
    private readonly HashSet<Hex> blightedLastRound = [];

    /// <summary>What a seat's orders cost in mana if they go through, stance and spell together (an Attune that clashes
    /// costs nothing).</summary>
    public int CostOf(Orders orders) => StanceCostOf(orders) + SpellCostOf(orders);

    /// <summary>What the stance part of a seat's orders costs.</summary>
    public int StanceCostOf(Orders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.Target is null ? 0 : orders.Stance switch
        {
            Stance.Attune => Rules.AttunePrice(seats[orders.Seat].Tiles.Count),
            Stance.Summon => Rules.CostOf(orders.Kind) * orders.Count,
            Stance.Build => Rules.UpgradeCost,
            _ => 0,
        };
    }

    /// <summary>What the spell cast alongside the stance costs, if any.</summary>
    public int SpellCostOf(Orders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.Cast is not { } cast ? 0 : Math.Max(0, cast.Spell switch
        {
            Spell.Scorch => Rules.ScorchManaPerPower * Burns(cast.Target, cast.Count),
            Spell.Ward => Rules.WardCost,
            Spell.Leyline => Math.Max(Rules.LeylineMinCost,
                Rules.LeylineManaPerUnit * ((cast.Transfer?.Units.Imps ?? 0) + (cast.Transfer?.Units.Golems ?? 0))),
            Spell.Blight => Rules.BlightCost,
            Spell.Lure => Rules.LureCost,
            Spell.Rally => Rules.RallyCost,
            _ => 0,
        } - SanctumDiscount(orders.Seat, cast.Target));
    }

    /// <summary>The power a Scorch of <paramref name="count"/> would burn at a tile as it stands: losses stop before a
    /// unit that would go past the count.</summary>
    private int Burns(Hex target, int count) =>
        UnitsOn(target).Power(Rules) - Battle.TakeLosses(UnitsOn(target), count, Rules).Power(Rules);

    /// <summary>Checks the spell cast alongside the stance; a Leyline updates <paramref name="positions"/> (where the
    /// seat's units stand), so the stance's attack is checked against where they will be.</summary>
    private string? SpellProblem(Orders orders, List<string> lines, Dictionary<Hex, Army> positions)
    {
        if (orders.Cast is not { } cast)
        {
            return null;
        }

        var target = cast.Target;
        if (!Map.Contains(target))
        {
            return $"{target} isn't on the map.";
        }

        var seat = seats[orders.Seat];
        var nextToYou = seat.Tiles.Any(h => h.IsNextTo(target));
        var owner = OwnerOf(target);
        var cost = 0;
        string effect;
        switch (cast.Spell)
        {
            case Spell.Scorch:
            {
                if (owner is null || owner == seat.Index || !nextToYou)
                {
                    return "Scorch needs a rival's tile next to yours.";
                }

                var army = UnitsOn(target);
                var power = army.Power(Rules);
                if (power == 0)
                {
                    return $"There is nothing at {target} to burn.";
                }

                if (cast.Count < 1 || cast.Count > power)
                {
                    return $"Scorch can burn from 1 to {power} power at {target}.";
                }

                if (Burns(target, cast.Count) == 0)
                {
                    // Losses stop before a unit that would go past the count: 1 can't burn a Golem.
                    return $"Scorch {cast.Count} would burn nothing at {target}: its weakest unit has power "
                           + $"{(army.Imps > 0 ? Rules.ImpPower : Rules.GolemPower)}.";
                }

                if (cast.Count > seat.Mana / Rules.ScorchManaPerPower)
                {
                    return $"Scorch costs {Rules.ScorchManaPerPower} mana per power; you have {seat.Mana}.";
                }

                var left = Battle.TakeLosses(army, cast.Count, Rules);
                var burns = power - left.Power(Rules);
                cost = Rules.ScorchManaPerPower * burns;
                effect = $"burn {army - left} at {target} (power {burns}), leaving {left}; you pay {Rules.ScorchManaPerPower} per power that burns, "
                         + "so less if units move away first";
                break;
            }

            case Spell.Ward:
            {
                // Spells come after Attunes, so a tile attuned this round can be warded (if the Attune goes through).
                var attuning = orders is { Stance: Stance.Attune, Target: { } attuned } && attuned == target;
                if (owner != seat.Index && !attuning)
                {
                    return "Ward needs one of your tiles, or the tile you attune this round.";
                }

                cost = Rules.WardCost;
                effect = $"{target} defends with {Rules.WardPower} more power this round" + (attuning ? ", if your Attune takes it" : "");
                break;
            }

            case Spell.Leyline:
            {
                if (cast.Transfer is not { } move || move.From != target || move.From == move.To
                    || !seat.Tiles.Contains(move.From) || !seat.Tiles.Contains(move.To))
                {
                    return "Leyline needs two different tiles of yours.";
                }

                // Units that step this round can't also ride the leyline; units summoned there this round can (spells
                // come after Summons).
                var summoned = orders is { Stance: Stance.Summon, Target: { } at, Count: > 0 } && at == move.From
                    ? Army.Of(orders.Kind, orders.Count)
                    : Army.None;
                var free = orders.Moves.Where(m => m.From == move.From).Aggregate(UnitsOn(move.From) + summoned, (a, m) => a - m.Units);
                if (!move.Units.IsValid || move.Units.IsEmpty || !free.Holds(move.Units))
                {
                    return $"There aren't {move.Units} at {move.From} to send (units that step this round, in or out, can't ride a Leyline too).";
                }

                cost = SpellCostOf(orders);
                effect = $"move {move.Units} from {move.From} to {move.To}";
                positions[move.From] = (positions.TryGetValue(move.From, out var p) ? p : UnitsOn(move.From)) - move.Units;
                positions[move.To] = (positions.TryGetValue(move.To, out var q) ? q : UnitsOn(move.To)) + move.Units;
                break;
            }

            case Spell.Blight:
                if (owner is null || owner == seat.Index || !nextToYou)
                {
                    return "Blight needs a rival's tile next to yours.";
                }

                if (blightedLastRound.Contains(target))
                {
                    return $"{target} was blighted last round; a tile can't be blighted two rounds running.";
                }

                cost = Rules.BlightCost;
                effect = $"{Describe(target)} yields nothing in the next round's income";
                break;

            case Spell.Lure:
                if (owner is not null || !Map[target].IsGuarded || !nextToYou)
                {
                    return "Lure needs a monster tile next to yours.";
                }

                if (Map[target].Guard <= Rules.MinGuard)
                {
                    return $"The guards at {target} can't be weakened any further.";
                }

                cost = Rules.LureCost;
                effect = $"the guards at {target} drop to power {Math.Max(Rules.MinGuard, Map[target].Guard - Rules.LurePower)}, for good";
                break;

            case Spell.Rally:
                if (orders is not { Stance: Stance.Attack, Target: { } attacked } || attacked != target)
                {
                    return "Rally needs the tile you attack this round.";
                }

                cost = Rules.RallyCost;
                effect = $"your attack on {target} strikes with {Rules.RallyPower} more power, and that power takes the blows first";
                break;

            default:
                return $"Unknown spell {cast.Spell}.";
        }

        // A Sanctum makes spells aimed at it or next to it cheaper (Leyline's cost already counts it).
        var sanctum = cast.Spell == Spell.Leyline ? 0 : Math.Min(cost, SanctumDiscount(orders.Seat, target));
        cost -= sanctum;
        if (cost > seat.Mana)
        {
            return $"{cast.Spell} costs {cost} mana; you have {seat.Mana}.";
        }

        lines.Add($"Cast {cast.Spell} for {cost} mana{(sanctum > 0 ? $" ({sanctum} less for your Sanctum)" : "")}: {effect}.");
        return null;
    }

    /// <summary>Every seat's spell, in seat order: they pay, and the spell takes effect.</summary>
    private void ResolveCasts(IEnumerable<Orders> orders, ReportLines lines)
    {
        foreach (var order in orders.Where(o => o.Cast is not null))
        {
            var seat = seats[order.Seat];
            var cast = order.Cast!;
            var target = cast.Target;
            seat.Mana -= cast.Spell == Spell.Scorch ? 0 : SpellCostOf(order);
            if (cast.Spell != Spell.Scorch)
            {
                seat.Cast(cast.Spell);
            }

            switch (cast.Spell)
            {
                case Spell.Scorch:
                    // Paid for what burns: units may have moved away, or a Golem be too strong to burn whole.
                    var before = UnitsOn(target);
                    units[target] = Battle.TakeLosses(before, cast.Count, Rules);
                    var burned = before - UnitsOn(target);
                    var paid = burned.IsEmpty ? 0 : Math.Max(0, Rules.ScorchManaPerPower * burned.Power(Rules) - SanctumDiscount(order.Seat, target));
                    seat.Mana -= paid;
                    if (!burned.IsEmpty)
                    {
                        seat.Cast(Spell.Scorch); // only a Scorch that burns counts for Hexer and Spellweaver
                    }

                    lines.Add($"Seat {seat.Index + 1} casts Scorch on {target}: {burned} burn, for {paid} mana.", seat.Index, target);
                    break;
                case Spell.Ward:
                    wards[target] = wards.GetValueOrDefault(target) + Rules.WardPower;
                    lines.Add($"Seat {seat.Index + 1} casts Ward on {target}: it defends with {Rules.WardPower} more power this round.", seat.Index, target);
                    break;
                case Spell.Leyline:
                    var move = cast.Transfer!;
                    units[move.From] = UnitsOn(move.From) - move.Units;
                    units[move.To] = UnitsOn(move.To) + move.Units;
                    lines.Add($"Seat {seat.Index + 1} casts Leyline: {move.Units} travel from {move.From} to {move.To}.", seat.Index, move.From, move.To);
                    break;
                case Spell.Blight:
                    blights.Add(target);
                    lines.Add($"Seat {seat.Index + 1} casts Blight on {Describe(target)}: it yields nothing next round.", seat.Index, target);
                    break;
                case Spell.Rally:
                    rallies[seat.Index] = rallies.GetValueOrDefault(seat.Index) + Rules.RallyPower;
                    lines.Add($"Seat {seat.Index + 1} casts Rally: its attack on {target} strikes with {Rules.RallyPower} more power.", seat.Index, target);
                    break;
                case Spell.Lure:
                    Map.SetGuard(target, Math.Max(Rules.MinGuard, Map[target].Guard - Rules.LurePower));
                    lines.Add($"Seat {seat.Index + 1} casts Lure on {target}: its guards drop to power {Map[target].Guard}.", seat.Index, target);
                    break;
            }
        }
    }

    private int Ward(Hex hex) => wards.GetValueOrDefault(hex);

    private int YieldFor(Hex hex) =>
        blights.Contains(hex) ? 0 : (Map[hex].Yield ?? 0) + (UpgradeOn(hex) == Upgrade.Shrine ? Rules.ShrineYield : 0);

    private void ClearRoundSpells()
    {
        blightedLastRound.Clear();
        blightedLastRound.UnionWith(blights);
        wards.Clear();
        rallies.Clear();
        blights.Clear();
    }
}
