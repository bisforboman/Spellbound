namespace Spellbound.Domain;

/// <summary>The Cast stance and its spells (docs/game-design.md, Spells). Spells resolve after moves, Attunes and
/// Summons, in seat order, and before attacks; Blight lands in the next round's income.</summary>
public sealed partial class Match
{
    // This round's Wards (extra defence by tile) and Blights (tiles that yield nothing in the next income). Both are
    // used up within the Resolve that makes them, so they are never saved.
    private readonly Dictionary<Hex, int> wards = [];
    private readonly HashSet<Hex> blights = [];

    /// <summary>What a seat's orders cost in mana if they go through (an Attune that clashes costs nothing).</summary>
    public int CostOf(Orders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (orders.Target is null)
        {
            return 0;
        }

        return orders.Stance switch
        {
            Stance.Attune => Rules.AttunePrice(seats[orders.Seat].Tiles.Count),
            Stance.Summon => Rules.CostOf(orders.Kind) * orders.Count,
            Stance.Cast => SpellCost(orders),
            _ => 0,
        };
    }

    private int SpellCost(Orders orders) => orders.Spell switch
    {
        Spell.Scorch => Rules.ScorchManaPerPower * orders.Count,
        Spell.Ward => Rules.WardCost,
        Spell.Leyline => Math.Max(Rules.LeylineMinCost,
            Rules.LeylineManaPerUnit * ((orders.Transfer?.Units.Imps ?? 0) + (orders.Transfer?.Units.Golems ?? 0))),
        Spell.Blight => Rules.BlightCost,
        Spell.Lure => Rules.LureCost,
        _ => 0,
    };

    private string? SpellProblem(Orders orders, Hex target, List<string> lines)
    {
        var seat = seats[orders.Seat];
        var nextToYou = seat.Tiles.Any(h => h.IsNextTo(target));
        var owner = OwnerOf(target);
        var cost = 0;
        string effect;
        switch (orders.Spell)
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

                if (orders.Count < 1 || orders.Count > power)
                {
                    return $"Scorch can burn from 1 to {power} power at {target}.";
                }

                if (orders.Count > seat.Mana / Rules.ScorchManaPerPower)
                {
                    return $"Scorch costs {Rules.ScorchManaPerPower} mana per power; you have {seat.Mana}.";
                }

                cost = SpellCost(orders);
                effect = $"burn {orders.Count} power of {army} at {target}, leaving {Battle.TakeLosses(army, orders.Count, Rules)}";
                break;
            }

            case Spell.Ward:
                if (owner != seat.Index)
                {
                    return "Ward needs one of your tiles.";
                }

                cost = Rules.WardCost;
                effect = $"{target} defends with {Rules.WardPower} more power this round";
                break;

            case Spell.Leyline:
            {
                if (orders.Transfer is not { } move || move.From != target || move.From == move.To
                    || !seat.Tiles.Contains(move.From) || !seat.Tiles.Contains(move.To))
                {
                    return "Leyline needs two different tiles of yours.";
                }

                // Units that step this round can't also ride the leyline.
                var free = orders.Moves.Where(m => m.From == move.From).Aggregate(UnitsOn(move.From), (a, m) => a - m.Units);
                if (!move.Units.IsValid || move.Units.IsEmpty || !free.Holds(move.Units))
                {
                    return $"There aren't {move.Units} at {move.From} to send.";
                }

                cost = SpellCost(orders);
                effect = $"move {move.Units} from {move.From} to {move.To}";
                break;
            }

            case Spell.Blight:
                if (owner is null || owner == seat.Index || !nextToYou)
                {
                    return "Blight needs a rival's tile next to yours.";
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

            default:
                return $"Unknown spell {orders.Spell}.";
        }

        if (cost > seat.Mana)
        {
            return $"{orders.Spell} costs {cost} mana; you have {seat.Mana}.";
        }

        lines.Add($"Cast {orders.Spell} for {cost} mana, leaving {seat.Mana - cost}: {effect}.");
        return null;
    }

    /// <summary>Every seat's spell, in seat order: they pay, and the spell takes effect.</summary>
    private void ResolveCasts(IEnumerable<Orders> casts, List<string> lines)
    {
        foreach (var order in casts)
        {
            var seat = seats[order.Seat];
            var target = order.Target!.Value;
            seat.Mana -= SpellCost(order);
            switch (order.Spell)
            {
                case Spell.Scorch:
                    var before = UnitsOn(target);
                    units[target] = Battle.TakeLosses(before, order.Count, Rules);
                    lines.Add($"Seat {seat.Index + 1} casts Scorch on {target}: {before - UnitsOn(target)} burn.");
                    break;
                case Spell.Ward:
                    wards[target] = wards.GetValueOrDefault(target) + Rules.WardPower;
                    lines.Add($"Seat {seat.Index + 1} casts Ward on {target}: it defends with {Rules.WardPower} more power this round.");
                    break;
                case Spell.Leyline:
                    var move = order.Transfer!;
                    units[move.From] = UnitsOn(move.From) - move.Units;
                    units[move.To] = UnitsOn(move.To) + move.Units;
                    lines.Add($"Seat {seat.Index + 1} casts Leyline: {move.Units} travel from {move.From} to {move.To}.");
                    break;
                case Spell.Blight:
                    blights.Add(target);
                    lines.Add($"Seat {seat.Index + 1} casts Blight on {Describe(target)}: it yields nothing next round.");
                    break;
                case Spell.Lure:
                    Map.SetGuard(target, Math.Max(Rules.MinGuard, Map[target].Guard - Rules.LurePower));
                    lines.Add($"Seat {seat.Index + 1} casts Lure on {target}: its guards drop to power {Map[target].Guard}.");
                    break;
            }
        }
    }

    private int Ward(Hex hex) => wards.GetValueOrDefault(hex);

    private int YieldFor(Hex hex) => blights.Contains(hex) ? 0 : Map[hex].Yield ?? 0;

    private void ClearRoundSpells()
    {
        wards.Clear();
        blights.Clear();
    }
}
