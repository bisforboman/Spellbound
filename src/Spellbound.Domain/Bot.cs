namespace Spellbound.Domain;

/// <summary>
/// A simple computer player (docs/roadmap.md, M1): a bid rule for the opening and an orders rule for the rounds. It
/// only ever gives orders that <see cref="Match.Check"/> accepts: it tries a few candidates in order and keeps the
/// first that checks, else it holds. Any variety comes from the injected random source, so games replay exactly.
/// </summary>
public static class Bot
{
    /// <summary>One of the traits the seat was offered, at random; null in a match without traits.</summary>
    public static Trait? ChooseTrait(Match match, int seat, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        var offer = match.Seats[seat].Offer;
        return offer.Count == 0 ? null : offer[random.Next(offer.Count)];
    }

    /// <summary>Bids on one of the best tiles on auction (within 1 of the best, picked at random): about its yield in
    /// mana, give or take one, never more than half its mana. A trait about land makes its tiles worth
    /// <see cref="TraitLean"/> more. Passes when it can't afford the smallest bid.</summary>
    public static Bid ChooseBid(Match match, int seat, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        var mana = match.Seats[seat].Mana;
        if (match.OnAuction.Count == 0 || mana < match.Rules.MinBid)
        {
            return Bid.Pass(seat);
        }

        var me = match.Seats[seat];
        var best = PickGood(match.OnAuction, h => match.Map[h].Yield!.Value + (Suits(match, me, h) ? TraitLean : 0), random);
        var amount = Math.Clamp(match.Map[best].Yield!.Value + random.Next(3) - 1, match.Rules.MinBid, Math.Max(match.Rules.MinBid, mana / 2));
        return new Bid(seat, best, amount);
    }

    /// <summary>In order: attack a weaker rival or monster tile next to its units; attune the best free tile next to
    /// its land; summon on a tile at its border; else hold.</summary>
    public static Orders ChooseOrders(Match match, int seat, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        var orders = Candidates(match, seat, random).FirstOrDefault(o => match.Check(o).Succeeded) ?? Orders.Hold(seat);

        // With mana to spare after the stance, a spell alongside it; sooner for a trait that scores spells.
        var spare = match.Seats[seat].Trait is Trait.Spellweaver or Trait.Hexer ? SpareMana / 2 : SpareMana;
        if (match.Seats[seat].Mana - match.CostOf(orders) < spare)
        {
            return orders;
        }

        var casts = Casts(match, seat);
        if (match.Seats[seat].Trait == Trait.Spellweaver)
        {
            // A spell it hasn't cast yet first.
            casts = casts.OrderBy(c => match.Seats[seat].SpellsCast.Contains(c.Spell));
        }

        return casts.Select(cast => orders with { Cast = cast }).FirstOrDefault(o => match.Check(o).Succeeded) ?? orders;
    }

    /// <summary>How much more a tile that suits the bot's trait is worth to it, in yield.</summary>
    private const int TraitLean = 2;

    /// <summary>Whether a tile suits the seat's trait, which is what its bids give away (docs/discussions/traits.md,
    /// the opening tells): its type; next to its land or away from it; next to rivals; next to the dark land where
    /// monsters will appear; or a rich yield to pay Golems' upkeep. A Hexer gives nothing away: land next to rivals
    /// gets it attacked, and it won least when it bid there.</summary>
    private static bool Suits(Match match, Seat me, Hex hex)
    {
        var neighbours = match.Map.NeighboursOf(hex).ToList();
        return me.Trait switch
        {
            Trait.Wildheart => match.Map[hex].Type == TileType.Wild,
            Trait.Hearthkeeper => match.Map[hex].Type == TileType.Steady,
            Trait.Weaver or Trait.Warden => me.Tiles.Any(hex.IsNextTo),
            Trait.Wanderer => me.Tiles.Count > 0 && !me.Tiles.Any(hex.IsNextTo),
            Trait.Warbrand or Trait.Spellweaver => neighbours.Any(n => match.OwnerOf(n) is { } o && o != me.Index),
            Trait.Beastbane => neighbours.Any(n => match.Map[n].IsGuarded) || neighbours.Count(n => !match.Map[n].IsUnveiled) >= 3,
            Trait.Golemwright => match.Map[hex].Yield >= 3,
            _ => false,
        };
    }

    /// <summary>One of the hexes scoring within 1 of the best, at random, so bots don't all reach for the same one.</summary>
    private static Hex PickGood(IReadOnlyCollection<Hex> hexes, Func<Hex, int> score, IRandomSource random)
    {
        var best = hexes.Max(score);
        var good = hexes.Where(h => score(h) >= best - 1).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        return good[random.Next(good.Count)];
    }

    /// <summary>The mana a bot keeps after its stance before it also casts a spell.</summary>
    private const int SpareMana = 6;

    /// <summary>Spells with a purpose, most urgent first: Ward a threatened tile; Lure or Scorch a target its units
    /// next to it can then beat; Blight a rival's best tile next to it.</summary>
    private static IEnumerable<SpellCast> Casts(Match match, int seat)
    {
        var rules = match.Rules;
        var mine = match.Seats[seat].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        int Power(Hex h) => match.UnitsOn(h).Power(rules);
        int MyPowerNextTo(Hex target) => mine.Where(target.IsNextTo).Sum(Power);
        var rivals = mine.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.OwnerOf(h) is { } o && o != seat).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();

        foreach (var threatened in mine.Where(h => match.Map.NeighboursOf(h).Any(n => rivals.Contains(n) && Power(n) > Power(h))))
        {
            yield return new SpellCast(Spell.Ward, threatened);
        }

        foreach (var monster in mine.SelectMany(match.Map.NeighboursOf).Distinct().OrderBy(h => h.Q).ThenBy(h => h.R)
                     .Where(h => match.OwnerOf(h) is null && match.Map[h].Guard > rules.MinGuard
                                 && MyPowerNextTo(h) >= match.Map[h].Guard - rules.LurePower))
        {
            yield return new SpellCast(Spell.Lure, monster);
        }

        foreach (var rival in rivals.Where(h => Power(h) > 0 && MyPowerNextTo(h) > 0 && MyPowerNextTo(h) <= Power(h)))
        {
            yield return new SpellCast(Spell.Scorch, rival, Power(rival) - MyPowerNextTo(rival) + 1);
        }

        foreach (var rival in rivals.Where(h => match.Map[h].Yield >= 3).OrderByDescending(h => match.Map[h].Yield))
        {
            yield return new SpellCast(Spell.Blight, rival);
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
            // A tie against monsters takes the tile; a Beastbane takes it.
            where army.Power(rules) > defence || (owner is null && me.Trait == Trait.Beastbane && army.Power(rules) == defence)
            orderby defence, target.Q, target.R
            select Orders.Attack(seat, target, [new Move(origin, target, army)]);
        foreach (var attack in attacks.Take(1))
        {
            yield return attack;
        }

        var free = mine.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null && !match.Map[h].IsGuarded).ToList();

        // Attune: one of the best free tiles next to its land, leaning to its trait's.
        var attune = free.Count == 0 ? null
            : Orders.Attune(seat, PickGood(free, h => match.Map[h].Yield!.Value + (Suits(match, me, h) ? TraitLean : 0), random));

        // Summon: half its mana (at least one unit's worth) on a tile next to a rival, monsters or free land.
        // A seat always holds a tile after the opening: the last one can't be taken.
        var border = mine.Where(h => match.Map.NeighboursOf(h).Any(n => match.OwnerOf(n) != seat)).ToList();
        var leaning = me.Trait switch
        {
            Trait.Beastbane => mine.Where(h => match.Map.NeighboursOf(h).Any(n => match.OwnerOf(n) is null && match.Map[n].IsGuarded)),
            Trait.Warden => border.Where(me.OpeningTiles.Contains),
            _ => [],
        };
        var at = leaning.Concat(border).FirstOrDefault(mine[0]);
        var kind = me.Trait is Trait.Golemwright or Trait.Beastbane || random.Next(3) == 0 ? UnitKind.Golem : UnitKind.Imp;
        var summons = new[]
        {
            Orders.Summon(seat, at, kind, Math.Max(1, me.Mana / 2 / rules.CostOf(kind))),
            Orders.Summon(seat, at, UnitKind.Imp, Math.Max(1, me.Mana / 2)),
        };

        // Every other round it summons before it attunes, so it has units to fight with.
        var unitsFirst = match.CurrentRound % 2 == 0;
        foreach (var orders in unitsFirst ? summons.Append(attune) : summons.Prepend(attune))
        {
            if (orders is not null)
            {
                yield return orders;
            }
        }
    }
}
