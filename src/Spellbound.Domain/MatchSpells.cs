namespace Spellbound.Domain;

/// <summary>Spells, cast with the Cast stance (docs/game-design.md, Spells). They resolve after moves, Attunes, Summons
/// and Builds, in seat order, and before attacks; Blight lands in the next round's income.</summary>
public sealed partial class Match
{
    // This round's Wards (extra defence by tile) and Blights (tiles that yield nothing in the next income). Both last
    // until the round ends, so they are saved while it is under way.
    private readonly Dictionary<Hex, int> wards = [];
    private readonly HashSet<Hex> blights = [];

    // The tiles blighted last round (they yielded nothing this round's income), which can't be blighted again now.
    private readonly HashSet<Hex> blightedLastRound = [];

    /// <summary>What a seat's orders cost in mana if they go through, an attack's reinforcements included (an Attune
    /// that clashes costs nothing).</summary>
    public int CostOf(Orders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.Target is null ? 0 : orders.Stance switch
        {
            Stance.Attune => AttuneBase(orders.Seat, orders.Target.Value) + 1, // the cheapest roll
            Stance.Summon => Rules.CostOf(orders.Kind) * orders.Count,
            Stance.Build => Rules.UpgradeCost,
            Stance.Cast => SpellCostOf(orders),
            _ => 0,
        } + (orders.Reinforce is { } r ? Rules.ReinforceCostOf(r.Kind, r.Count) : 0);
    }

    /// <summary>What a Cast's spell costs on all its tiles (0 for any other stance), a Sanctum's discount taken off.</summary>
    public int SpellCostOf(Orders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.Stance != Stance.Cast ? 0 : orders.SpellTiles.Sum(tile => SpellCostOn(orders, tile));
    }

    /// <summary>The spells a seat can cast: its trait's (docs/game-design.md, Spells), or every one without a trait.</summary>
    public IReadOnlyList<Spell> SpellsOf(int seat) =>
        seats[seat].Trait is { } trait ? Rules.TraitSpells.GetValueOrDefault(trait) ?? [] : GameRules.AllSpells;

    /// <summary>What a spell costs, in words, with this match's numbers.</summary>
    public string SpellCostText(Spell spell) => spell switch
    {
        Spell.Scorch => $"{Rules.ScorchManaPerPower} mana per power that burns",
        Spell.Ward => $"{Rules.WardCost} mana a tile",
        Spell.Leyline => $"{Rules.LeylineManaPerUnit} mana per unit, at least {Rules.LeylineMinCost}",
        Spell.Blight => $"{Rules.BlightCost} mana a tile",
        Spell.Lure => $"{Rules.LureCost} mana a tile",
        _ => "nothing",
    };

    /// <summary>What a spell does, in words, with this match's numbers.</summary>
    public string SpellEffect(Spell spell) => spell switch
    {
        Spell.Scorch => "burn units on a rival's tile next to yours",
        Spell.Ward => $"your tiles defend with {Rules.WardPower} more power this round",
        Spell.Leyline => "send units between any two of your tiles",
        Spell.Blight => "rivals' tiles next to yours yield nothing next round",
        Spell.Lure => $"monster tiles next to yours lose {Rules.LurePower} guard power for good",
        _ => "nothing",
    };

    /// <summary>The spells that can go on several tiles in one Cast; Scorch and Leyline already grow with the mana put in.</summary>
    public static bool GoesOnSeveralTiles(Spell spell) => spell is Spell.Ward or Spell.Blight or Spell.Lure;

    private int SpellCostOn(Orders orders, Hex target)
    {
        return Math.Max(0, orders.Spell switch
        {
            Spell.Scorch => Rules.ScorchManaPerPower * Burns(target, orders.Count),
            Spell.Ward => Rules.WardCost,
            Spell.Leyline => Math.Max(Rules.LeylineMinCost,
                Rules.LeylineManaPerUnit * ((orders.Transfer?.Units.Imps ?? 0) + (orders.Transfer?.Units.Golems ?? 0))),
            Spell.Blight => Rules.BlightCost,
            Spell.Lure => Rules.LureCost,
            _ => 0,
        } - SanctumDiscount(orders.Seat, target));
    }

    /// <summary>The power a Scorch of <paramref name="count"/> would burn at a tile as it stands: losses stop before a
    /// unit that would go past the count.</summary>
    private int Burns(Hex target, int count) =>
        UnitsOn(target).Power(Rules) - Battle.TakeLosses(UnitsOn(target), count, Rules).Power(Rules);

    /// <summary>Checks a Cast stance's spell: one of the seat's, on <paramref name="target"/> (already known to be on
    /// the map) and any more tiles, each checked alone and all paid for together.</summary>
    private string? SpellProblem(Orders orders, Hex target, List<string> lines)
    {
        var seat = seats[orders.Seat];
        if (!SpellsOf(seat.Index).Contains(orders.Spell))
        {
            return orders.Spell == Spell.Rally ? "Rally is no longer a spell."
                : $"{orders.Spell} isn't one of your spells; yours: {string.Join(", ", SpellsOf(seat.Index))}.";
        }

        var tiles = orders.SpellTiles;
        if (tiles.Count == 1)
        {
            return OneSpellProblem(orders, target, lines, alone: true, out _);
        }

        if (!GoesOnSeveralTiles(orders.Spell))
        {
            return $"Only Ward, Blight and Lure go on several tiles at once; {orders.Spell} goes on one.";
        }

        if (tiles.Distinct().Count() != tiles.Count)
        {
            return $"Each tile takes {orders.Spell} once.";
        }

        var total = 0;
        foreach (var tile in tiles)
        {
            if (!Map.Contains(tile))
            {
                return $"{tile} isn't on the map.";
            }

            if (OneSpellProblem(orders, tile, lines, alone: false, out var cost) is { } problem)
            {
                return problem;
            }

            total += cost;
        }

        if (total > seat.Mana)
        {
            return $"{orders.Spell} on {tiles.Count} tiles costs {total} mana; you have {seat.Mana}.";
        }

        lines.Add($"All together: {orders.Spell} on {tiles.Count} tiles for {total} mana, leaving {seat.Mana - total}.");
        return null;
    }

    /// <summary>Checks the spell on one tile, and says what it costs there.</summary>
    private string? OneSpellProblem(Orders orders, Hex target, List<string> lines, bool alone, out int cost)
    {
        cost = 0;
        var seat = seats[orders.Seat];
        var nextToYou = seat.Tiles.Any(h => h.IsNextTo(target));
        var owner = OwnerOf(target);
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

                if (Burns(target, orders.Count) == 0)
                {
                    // Losses stop before a unit that would go past the count: 1 can't burn a Golem.
                    return $"Scorch {orders.Count} would burn nothing at {target}: its weakest unit has power "
                           + $"{(army.Imps > 0 ? Rules.ImpPower : Rules.GolemPower)}.";
                }

                if (orders.Count > seat.Mana / Rules.ScorchManaPerPower)
                {
                    return $"Scorch costs {Rules.ScorchManaPerPower} mana per power; you have {seat.Mana}.";
                }

                var left = Battle.TakeLosses(army, orders.Count, Rules);
                var burns = power - left.Power(Rules);
                cost = Rules.ScorchManaPerPower * burns;
                effect = $"burn {army - left} at {target} (power {burns}), leaving {left}; you pay {Rules.ScorchManaPerPower} per power that burns, "
                         + "so less if units move away first";
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
                    return $"There aren't {move.Units} at {move.From} to send (units that step this round, in or out, can't ride a Leyline too).";
                }

                cost = SpellCostOn(orders, target);
                effect = $"move {move.Units} from {move.From} to {move.To}";
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

            default:
                return $"Unknown spell {orders.Spell}.";
        }

        // A Sanctum makes spells aimed at it or next to it cheaper (Leyline's cost already counts it).
        var sanctum = orders.Spell == Spell.Leyline ? 0 : Math.Min(cost, SanctumDiscount(orders.Seat, target));
        cost -= sanctum;
        if (cost > seat.Mana)
        {
            return $"{orders.Spell} costs {cost} mana; you have {seat.Mana}.";
        }

        lines.Add($"Cast {orders.Spell} for {cost} mana{(sanctum > 0 ? $" ({sanctum} less for your Sanctum)" : "")}"
                  + (alone ? $", leaving {seat.Mana - cost}" : "") + $": {effect}.");
        return null;
    }

    /// <summary>Every seat's spell, in seat order: they pay, and the spell takes effect.</summary>
    private void ResolveCasts(IEnumerable<Orders> orders, ReportLines lines)
    {
        foreach (var order in orders.Where(o => o is { Stance: Stance.Cast, Target: not null }))
        {
            var seat = seats[order.Seat];
            seat.Mana -= order.Spell == Spell.Scorch ? 0 : SpellCostOf(order);
            if (order.Spell != Spell.Scorch)
            {
                seat.Cast(order.Spell, CurrentRound); // once a Cast, however many tiles it goes on
            }

            foreach (var target in order.SpellTiles)
            {
                switch (order.Spell)
                {
                    case Spell.Scorch:
                        // Paid for what burns: units may have moved away, or a Golem be too strong to burn whole.
                        var before = UnitsOn(target);
                        units[target] = Battle.TakeLosses(before, order.Count, Rules);
                        var burned = before - UnitsOn(target);
                        var paid = burned.IsEmpty ? 0 : Math.Max(0, Rules.ScorchManaPerPower * burned.Power(Rules) - SanctumDiscount(order.Seat, target));
                        seat.Mana -= paid;
                        if (!burned.IsEmpty)
                        {
                            seat.Cast(Spell.Scorch, CurrentRound); // only a Scorch that burns counts for Hexer and Spellweaver
                        }

                        lines.Add($"Seat {seat.Index + 1} casts Scorch on {target}: {burned} burn, for {paid} mana.", seat.Index, target);
                        break;
                    case Spell.Ward:
                        wards[target] = wards.GetValueOrDefault(target) + Rules.WardPower;
                        lines.Add($"Seat {seat.Index + 1} casts Ward on {target}: it defends with {Rules.WardPower} more power this round.", seat.Index, target);
                        break;
                    case Spell.Leyline:
                        var move = order.Transfer!;
                        units[move.From] = UnitsOn(move.From) - move.Units;
                        units[move.To] = UnitsOn(move.To) + move.Units;
                        lines.Add($"Seat {seat.Index + 1} casts Leyline: {move.Units} travel from {move.From} to {move.To}.", seat.Index, move.From, move.To);
                        break;
                    case Spell.Blight:
                        blights.Add(target);
                        lines.Add($"Seat {seat.Index + 1} casts Blight on {Describe(target)}: it yields nothing next round.", seat.Index, target);
                        break;
                    case Spell.Lure:
                        Map.SetGuard(target, Math.Max(Rules.MinGuard, Map[target].Guard - Rules.LurePower));
                        lines.Add($"Seat {seat.Index + 1} casts Lure on {target}: its guards drop to power {Map[target].Guard}.", seat.Index, target);
                        break;
                }
            }
        }
    }

    private int Ward(Hex hex) => wards.GetValueOrDefault(hex);

    /// <summary>What a tile paid its holder at the start of this round: its roll and a Shrine's extra, or nothing if it
    /// was blighted. For the page's "+N" on each tile.</summary>
    public int IncomeOf(Hex hex) =>
        blightedLastRound.Contains(hex) ? 0 : (Map[hex].Yield ?? 0) + (UpgradeOn(hex) == Upgrade.Shrine ? Rules.ShrineYield : 0);

    private int YieldFor(Hex hex) =>
        blights.Contains(hex) ? 0 : (Map[hex].Yield ?? 0) + (UpgradeOn(hex) == Upgrade.Shrine ? Rules.ShrineYield : 0);

    private void ClearRoundSpells()
    {
        blightedLastRound.Clear();
        blightedLastRound.UnionWith(blights);
        wards.Clear();
        blights.Clear();
    }
}
