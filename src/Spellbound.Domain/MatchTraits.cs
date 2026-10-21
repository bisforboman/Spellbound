namespace Spellbound.Domain;

/// <summary>Traits (docs/game-design.md, Traits): each seat is offered its own few, drafts one in secret during the
/// opening, and its goal is scored once, at the end.</summary>
public sealed partial class Match
{
    /// <summary>Deals every seat its own offer from the pool, no trait in two offers; none if the pool is too small for
    /// this many seats.</summary>
    private void DealTraits(IRandomSource random)
    {
        var pool = Enum.GetValues<Trait>().ToList();
        if (Rules.TraitsOffered < 1 || pool.Count < seats.Count * Rules.TraitsOffered)
        {
            return;
        }

        foreach (var seat in seats)
        {
            var offer = new List<Trait>();
            for (var i = 0; i < Rules.TraitsOffered; i++)
            {
                var pick = random.Next(pool.Count);
                offer.Add(pool[pick]);
                pool.RemoveAt(pick);
            }

            seat.Offer = offer;
        }
    }

    /// <summary>Drafts one of the traits a seat was offered. Only during the opening, and only once; fails, changing
    /// nothing, otherwise.</summary>
    public Report ChooseTrait(int seat, Trait trait)
    {
        if (seat < 0 || seat >= seats.Count)
        {
            return Report.Fail($"There is no seat {seat + 1}.");
        }

        var who = seats[seat];
        if (Phase != MatchPhase.Opening)
        {
            return Report.Fail("Traits are chosen during the opening.");
        }

        if (who.Trait is { } chosen)
        {
            return Report.Fail($"Seat {seat + 1} already chose {chosen}.");
        }

        if (!who.Offer.Contains(trait))
        {
            return Report.Fail($"{trait} isn't one of the traits Seat {seat + 1} was offered.");
        }

        who.Trait = trait;
        return new Report(true, [$"Seat {seat + 1} chooses {trait}: {GoalText(trait)}"]);
    }

    /// <summary>What a trait's goal pays, in words, with this match's numbers.</summary>
    public string GoalText(Trait trait)
    {
        var r = Rules;
        var goal = trait switch
        {
            Trait.Wildheart => $"1 point per {r.TilesOfTypePerTraitPoint} Wild tiles held",
            Trait.Hearthkeeper => $"1 point per {r.TilesOfTypePerTraitPoint} Steady tiles held",
            Trait.Weaver => $"1 point per {r.GroupTilesPerTraitPoint} tiles in your largest connected group",
            Trait.Wanderer => "1 point per separate group of tiles beyond the first",
            Trait.Beastbane => "1 point per monster tile cleared",
            Trait.Warbrand => "1 point per rival tile taken",
            Trait.Golemwright => $"1 point per {r.GolemsPerTraitPoint} Golems you have",
            Trait.Spellweaver => "1 point per different spell cast beyond the first",
            Trait.Warden => "1 point per tile from your opening you still hold, beyond the first",
            Trait.Hexer => $"1 point per {r.HexesPerTraitPoint} Scorches and Blights cast",
            _ => "nothing",
        };
        return $"at the end, {goal}, up to {r.TraitGoalCap}.";
    }

    /// <summary>A seat's trait goal: what it counts and the points, capped.</summary>
    public (int Count, int Points) TraitGoal(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        var r = Rules;
        var tiles = seat.Tiles;
        var groups = Groups(tiles);
        var golems = tiles.Sum(h => UnitsOn(h).Golems);
        (int Count, int Points) goal = seat.Trait switch
        {
            Trait.Wildheart => Per(tiles.Count(h => Map[h].Type == TileType.Wild), r.TilesOfTypePerTraitPoint),
            Trait.Hearthkeeper => Per(tiles.Count(h => Map[h].Type == TileType.Steady), r.TilesOfTypePerTraitPoint),
            Trait.Weaver => Per(groups.Count == 0 ? 0 : groups.Max(g => g.Count), r.GroupTilesPerTraitPoint),
            Trait.Wanderer => (groups.Count, Math.Max(0, groups.Count - 1)),
            Trait.Beastbane => Per(seat.MonstersCleared, 1),
            Trait.Warbrand => Per(seat.RivalTilesTaken, 1),
            Trait.Golemwright => Per(golems, r.GolemsPerTraitPoint),
            Trait.Spellweaver => (seat.SpellsCast.Count, Math.Max(0, seat.SpellsCast.Count - 1)),
            Trait.Warden => (seat.OpeningTiles.Count(tiles.Contains), Math.Max(0, seat.OpeningTiles.Count(tiles.Contains) - 1)),
            Trait.Hexer => Per(seat.HexesCast, r.HexesPerTraitPoint),
            _ => (0, 0),
        };
        return (goal.Count, Math.Min(r.TraitGoalCap, goal.Points));

        static (int, int) Per(int count, int per) => (count, count / per);
    }

    /// <summary>At the end: every seat's trait is revealed with its goal, and the goal's points are added.</summary>
    private void ScoreTraits(List<string> lines)
    {
        foreach (var seat in seats.Where(s => s.Trait is not null))
        {
            var (count, points) = TraitGoal(seat);
            seat.Score += points;
            seat.TraitPoints = points;
            lines.Add($"Seat {seat.Index + 1}'s trait, {seat.Trait}: {GoalCount(seat.Trait!.Value, count)}, "
                      + $"{Plural(points, "point")}, {seat.Score} in all.");
        }
    }

    private static string GoalCount(Trait trait, int count) => trait switch
    {
        Trait.Wildheart => Plural(count, "Wild tile"),
        Trait.Hearthkeeper => Plural(count, "Steady tile"),
        Trait.Weaver => $"a group of {Plural(count, "tile")}",
        Trait.Wanderer => Plural(count, "separate group"),
        Trait.Beastbane => Plural(count, "monster tile") + " cleared",
        Trait.Warbrand => Plural(count, "rival tile") + " taken",
        Trait.Golemwright => Plural(count, "Golem"),
        Trait.Spellweaver => Plural(count, "different spell"),
        Trait.Warden => Plural(count, "opening tile") + " held",
        Trait.Hexer => Plural(count, "Scorch or Blight", "Scorches and Blights"),
        _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>At the end of the opening: a seat that hasn't chosen gets the first trait of its offer.</summary>
    private void SettleTraits(List<string> lines)
    {
        foreach (var seat in seats)
        {
            seat.OpeningTiles = [.. seat.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R)];
        }

        foreach (var seat in seats.Where(s => s.Offer.Count > 0))
        {
            seat.Trait ??= seat.Offer[0];
            lines.Add($"Seat {seat.Index + 1}'s trait is {seat.Trait}.");
        }
    }

    /// <summary>A set of tiles split into groups that touch.</summary>
    private static List<List<Hex>> Groups(IReadOnlySet<Hex> tiles)
    {
        var left = tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var groups = new List<List<Hex>>();
        while (left.Count > 0)
        {
            var group = new List<Hex> { left[0] };
            left.RemoveAt(0);
            for (var i = 0; i < group.Count; i++)
            {
                var touching = left.Where(group[i].IsNextTo).ToList();
                group.AddRange(touching);
                left.RemoveAll(touching.Contains);
            }

            groups.Add(group);
        }

        return groups;
    }
}
