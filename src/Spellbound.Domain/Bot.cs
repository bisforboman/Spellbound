namespace Spellbound.Domain;

/// <summary>
/// A simple computer player (docs/roadmap.md, M1): a bid rule for the opening and an orders rule for the rounds. It
/// only ever gives orders that <see cref="Match.Check"/> accepts: it tries a few candidates in order and keeps the
/// first that checks, else it holds. Any variety comes from the injected random source, so games replay exactly.
/// </summary>
public static class Bot
{
    /// <summary>Bids on one of the best-yielding tiles on auction (within 1 of the best, picked at random): about its
    /// yield in mana, give or take one, never more than half its mana. Passes when it can't afford the smallest
    /// bid.</summary>
    public static Bid ChooseBid(Match match, int seat, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        var mana = match.Seats[seat].Mana;
        if (match.OnAuction.Count == 0 || mana < match.Rules.MinBid)
        {
            return Bid.Pass(seat);
        }

        var best = PickGood(match.OnAuction, h => match.Map[h].Yield!.Value, random);
        var amount = Math.Clamp(match.Map[best].Yield!.Value + random.Next(3) - 1, match.Rules.MinBid, Math.Max(match.Rules.MinBid, mana / 2));
        return new Bid(seat, best, amount);
    }

    /// <summary>In order: attack a weaker rival or monster tile next to its units; attune the best free tile next to
    /// its land; summon on a tile at its border; else hold.</summary>
    public static Orders ChooseOrders(Match match, int seat, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        return Candidates(match, seat, random).FirstOrDefault(o => match.Check(o).Succeeded) ?? Orders.Hold(seat);
    }

    /// <summary>One of the hexes scoring within 1 of the best, at random, so bots don't all reach for the same one.</summary>
    private static Hex PickGood(IReadOnlyCollection<Hex> hexes, Func<Hex, int> score, IRandomSource random)
    {
        var best = hexes.Max(score);
        var good = hexes.Where(h => score(h) >= best - 1).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        return good[random.Next(good.Count)];
    }

    /// <summary>The mana a bot keeps beyond its next Attune before it casts instead.</summary>
    private const int SpareMana = 6;

    /// <summary>Spells with a purpose, most urgent first: Ward a threatened tile; Lure or Scorch a target its units
    /// next to it can then beat; Blight a rival's best tile next to it.</summary>
    private static IEnumerable<Orders> Casts(Match match, int seat)
    {
        var rules = match.Rules;
        var mine = match.Seats[seat].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        int Power(Hex h) => match.UnitsOn(h).Power(rules);
        int MyPowerNextTo(Hex target) => mine.Where(target.IsNextTo).Sum(Power);
        var rivals = mine.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.OwnerOf(h) is { } o && o != seat).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();

        foreach (var threatened in mine.Where(h => match.Map.NeighboursOf(h).Any(n => rivals.Contains(n) && Power(n) > Power(h))))
        {
            yield return Orders.Cast(seat, Spell.Ward, threatened);
        }

        foreach (var monster in mine.SelectMany(match.Map.NeighboursOf).Distinct().OrderBy(h => h.Q).ThenBy(h => h.R)
                     .Where(h => match.OwnerOf(h) is null && match.Map[h].Guard > rules.MinGuard
                                 && MyPowerNextTo(h) >= match.Map[h].Guard - rules.LurePower))
        {
            yield return Orders.Cast(seat, Spell.Lure, monster);
        }

        foreach (var rival in rivals.Where(h => Power(h) > 0 && MyPowerNextTo(h) > 0 && MyPowerNextTo(h) <= Power(h)))
        {
            yield return Orders.Cast(seat, Spell.Scorch, rival, Power(h: rival) - MyPowerNextTo(rival) + 1);
        }

        foreach (var rival in rivals.Where(h => match.Map[h].Yield >= 3).OrderByDescending(h => match.Map[h].Yield))
        {
            yield return Orders.Cast(seat, Spell.Blight, rival);
        }
    }

    private static IEnumerable<Orders> Candidates(Match match, int seat, IRandomSource random)
    {
        var rules = match.Rules;
        var me = match.Seats[seat];
        var mine = me.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();

        // Attack: the weakest target it can beat, sending everything from one tile.
        var attacks =
            from origin in mine
            let army = match.UnitsOn(origin)
            where !army.IsEmpty
            from target in match.Map.NeighboursOf(origin)
            let owner = match.OwnerOf(target)
            where owner != seat && (owner is not null || match.Map[target].IsGuarded)
            let defence = owner is null ? match.Map[target].Guard : match.UnitsOn(target).Power(rules)
            where army.Power(rules) > defence
            orderby defence, target.Q, target.R
            select Orders.Attack(seat, target, [new Move(origin, target, army)]);
        foreach (var attack in attacks.Take(1))
        {
            yield return attack;
        }

        // With mana to spare beyond the next Attune, a spell first; otherwise Attune, then a spell before summoning.
        var free = mine.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null && !match.Map[h].IsGuarded).ToList();
        var flush = me.Mana >= rules.AttunePrice(me.Tiles.Count) + SpareMana;
        if (flush)
        {
            foreach (var cast in Casts(match, seat).Where(o => o.Spell != Spell.Blight))
            {
                yield return cast;
            }
        }

        // Attune: one of the best-yielding free tiles next to its land.
        if (free.Count > 0)
        {
            yield return Orders.Attune(seat, PickGood(free, h => match.Map[h].Yield!.Value, random));
        }

        if (!flush && me.Mana >= SpareMana)
        {
            foreach (var cast in Casts(match, seat))
            {
                yield return cast;
            }
        }

        // Summon: half its mana (at least one unit's worth) on a tile next to a rival, monsters or free land.
        // A seat always holds a tile after the opening: the last one can't be taken.
        var at = mine.FirstOrDefault(h => match.Map.NeighboursOf(h).Any(n => match.OwnerOf(n) != seat), mine[0]);
        var kind = random.Next(3) == 0 ? UnitKind.Golem : UnitKind.Imp;
        yield return Orders.Summon(seat, at, kind, Math.Max(1, me.Mana / 2 / rules.CostOf(kind)));
        yield return Orders.Summon(seat, at, UnitKind.Imp, Math.Max(1, me.Mana / 2));
    }
}
