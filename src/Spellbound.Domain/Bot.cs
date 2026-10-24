namespace Spellbound.Domain;

/// <summary>A bot's plan: what it puts first (docs/discussions/strategies.md). Every plan defends a threatened tile and
/// only gives orders that check; they differ in priorities and in how freely they spend.</summary>
public enum Plan
{
    /// <summary>The everyday bot: attack what it can beat, else attune, cast or summon, building with spare mana.</summary>
    Balanced,

    /// <summary>Armies first: summons big and attacks whenever it can win; Scorch or Lure for its next attack.</summary>
    Warlord,

    /// <summary>An engine: builds upgrades early, attunes, fights only to defend.</summary>
    Builder,

    /// <summary>A spell every round it can afford one; a Sanctum first.</summary>
    Spellcaster,

    /// <summary>Land first: attunes every round it can.</summary>
    Expander,

    /// <summary>Income first: rich land, Shrines, and guarding what yields most.</summary>
    Economist,
}

/// <summary>
/// A simple computer player (docs/roadmap.md, M1): a pick rule for the opening and an orders rule for the rounds. It
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

    /// <summary>Picks the tile worth most to it: its average yield, and <see cref="TraitLean"/> more for a tile its
    /// trait wants; a tie is drawn at random. Null when it isn't the seat's turn.</summary>
    public static Pick? ChoosePick(Match match, int seat, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        if (match.Picker != seat || match.ToPick.Count == 0)
        {
            return null;
        }

        var me = match.Seats[seat];
        int Value(Hex h) => Worth(match, h) + (Suits(match, me, h) ? TraitLean : 0);
        var best = match.ToPick.Where(h => Value(h) == match.ToPick.Max(Value)).ToList();
        return new Pick(seat, best[random.Next(best.Count)]);
    }

    /// <summary>In order: attack a weaker rival or monster tile next to its units; attune the best free tile next to
    /// its land; cast a spell with a purpose, or summon on a tile at its border; else hold.</summary>
    public static Orders ChooseOrders(Match match, int seat, IRandomSource random, Plan plan = Plan.Balanced)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);
        var candidates = plan == Plan.Balanced ? Candidates(match, seat, random) : PlanCandidates(match, seat, random, plan);
        var lastRound = match.CurrentRound == match.Rules.Rounds;
        if (lastRound)
        {
            // Mana left at the end scores nothing: take a tile if it can, else buy an upgrade (a point) before the rest.
            candidates = new[] { BestAttack(match, seat, match.Seats[seat]), LastUpgrade(match, seat) }.OfType<Orders>().Concat(candidates);
        }

        var orders = candidates.FirstOrDefault(o => match.Check(o).Succeeded) ?? Orders.Hold(seat);
        if (orders is { Stance: Stance.Attack, Attackers: [var first, ..] })
        {
            // The army leaves its tile empty: with mana to spare, a few Imps guard it.
            var guarded = orders.Reinforcing(first.From, UnitKind.Imp, match.Rules.ReinforceMaxPower / match.Rules.ImpPower);
            if (match.Seats[seat].Mana - match.CostOf(guarded) >= SpareMana && match.Check(guarded).Succeeded)
            {
                orders = guarded;
            }
        }

        return orders;
    }

    /// <summary>An upgrade on its best-yielding open tile, if it has room for one; null if not.</summary>
    private static Orders? LastUpgrade(Match match, int seat)
    {
        var me = match.Seats[seat];
        var open = me.Tiles.Where(h => match.UpgradeOn(h) is null).OrderByDescending(h => Worth(match, h))
            .ThenBy(h => h.Q).ThenBy(h => h.R).ToList();
        return open.Count == 0 || match.UpgradesOf(me) >= match.Rules.UpgradeCap ? null : Orders.Build(seat, open[0], Upgrade.Shrine);
    }

    /// <summary>How much more a tile that suits the bot's trait is worth to it, in yield.</summary>
    private const int TraitLean = 2;

    /// <summary>Whether a tile suits the seat's trait, which is what its picks give away (docs/discussions/traits.md,
    /// the opening tells): its type; next to its land or away from it; next to rivals; next to the dark land where
    /// monsters will appear; or a rich yield to pay Golems' upkeep. A Hexer gives nothing away: land next to rivals
    /// gets it attacked, and it won least when it took land there.</summary>
    private static bool Suits(Match match, Seat me, Hex hex) => Suits(match, me, me.Trait, hex);

    private static bool Suits(Match match, Seat me, Trait? trait, Hex hex)
    {
        var neighbours = match.Map.NeighboursOf(hex).ToList();
        return trait switch
        {
            Trait.Wildheart => match.Map[hex].Type == TileType.Wild,
            Trait.Hearthkeeper => match.Map[hex].Type == TileType.Steady,
            Trait.Weaver or Trait.Warden => me.Tiles.Any(hex.IsNextTo),
            Trait.Wanderer => me.Tiles.Count > 0 && !me.Tiles.Any(hex.IsNextTo),
            Trait.Warbrand or Trait.Spellweaver => neighbours.Any(n => match.OwnerOf(n) is { } o && o != me.Index),
            Trait.Beastbane => neighbours.Any(n => match.Map[n].IsGuarded) || neighbours.Count(n => !match.Map[n].IsUnveiled) >= 3,
            Trait.Golemwright => match.Map[hex].Type == TileType.Steady, // a sure yield for the upkeep
            _ => false,
        };
    }

    /// <summary>One of the hexes scoring within 1 of the best, at random, so bots don't all reach for the same one.</summary>
    /// <summary>What a tile yields on average: the bots weigh tiles by it, since a tile rolls its yield each round.</summary>
    private static int Worth(Match match, Hex hex) => (int)Math.Round(match.Rules.AverageYield(match.Map[hex].Type));

    /// <summary>A tile's average income, a Shrine's included.</summary>
    private static int Income(Match match, Hex hex) =>
        Worth(match, hex) + (match.UpgradeOn(hex) == Upgrade.Shrine ? match.Rules.ShrineYield : 0);

    private static Hex PickGood(IReadOnlyCollection<Hex> hexes, Func<Hex, int> score, IRandomSource random)
    {
        var best = hexes.Max(score);
        var good = hexes.Where(h => score(h) >= best - 1).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        return good[random.Next(good.Count)];
    }

    /// <summary>The mana a bot keeps beyond an upgrade's price before it builds one.</summary>
    private const int BuildSpare = 6;

    /// <summary>The mana a bot keeps after an attack before it also brings reinforcements.</summary>
    private const int SpareMana = 6;

    /// <summary>Casts with a purpose, among the seat's own spells, as the plan orders them: a Warlord scorches and lures
    /// first (its next attack), an Economist wards and blights first (income, its own and its rivals'), and a
    /// Spellcaster or a Spellweaver tries a spell it hasn't cast yet first. A Ward, Blight or Lure goes on every tile
    /// with a purpose at once, else on the first.</summary>
    private static IEnumerable<Orders> Casts(Match match, int seat, Plan plan)
    {
        var me = match.Seats[seat];
        var mine = match.SpellsOf(seat);
        var casts = Spells(match, seat).Where(c => mine.Contains(c.Spell)).ToList();
        IEnumerable<Orders> ordered = plan switch
        {
            Plan.Warlord => casts.OrderBy(c => c.Spell is not (Spell.Scorch or Spell.Lure)),
            Plan.Economist => casts.OrderBy(c => c.Spell is not (Spell.Ward or Spell.Blight)),
            Plan.Spellcaster => casts.OrderBy(c => me.SpellsCast.Contains(c.Spell)),
            _ => casts,
        };
        if (me.Trait == Trait.Spellweaver)
        {
            ordered = ordered.OrderBy(c => me.SpellsCast.Contains(c.Spell));
        }

        foreach (var cast in ordered)
        {
            var more = casts.Where(c => c.Spell == cast.Spell && c.Target != cast.Target).Select(c => c.Target!.Value).Distinct().ToList();
            if (Match.GoesOnSeveralTiles(cast.Spell) && more.Count > 0)
            {
                yield return cast with { MoreTiles = more };
            }

            yield return cast;
        }
    }

    /// <summary>Spells with a purpose, most urgent first: Ward a threatened tile; Lure or Scorch a target its units
    /// next to it can then beat next round; Blight a rival's best tile next to it.</summary>
    private static IEnumerable<Orders> Spells(Match match, int seat)
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
            yield return Orders.Cast(seat, Spell.Scorch, rival, Power(rival) - MyPowerNextTo(rival) + 1);
        }

        foreach (var rival in rivals.Where(h => Income(match, h) >= 3 || match.Map[h].Type == TileType.Wild) // a Shrine, or a tile that may roll high
                     .OrderByDescending(h => Income(match, h)).ThenBy(h => h.Q).ThenBy(h => h.R))
        {
            yield return Orders.Cast(seat, Spell.Blight, rival);
        }
    }

    /// <summary>A specialist plan's candidates, most wanted first. Each starts from the same pieces as the everyday bot
    /// (the best attack, the best Attune, a summon at the border, an upgrade) and a defence of its most threatened tile;
    /// the plan only orders them and sets how much it spends.</summary>
    private static IEnumerable<Orders> PlanCandidates(Match match, int seat, IRandomSource random, Plan plan)
    {
        var rules = match.Rules;
        var me = match.Seats[seat];
        var mine = me.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        int Power(Hex h) => match.UnitsOn(h).Power(rules);

        var attack = BestAttack(match, seat, me);
        var monsterAttack = attack is { Target: { } t } && match.OwnerOf(t) is null ? attack : null;

        // Defend: enough Imps on its most threatened tile to beat the rival stack next to it.
        var threats = mine.Select(h => (Tile: h, Gap: match.Map.NeighboursOf(h)
                .Where(n => match.OwnerOf(n) is { } o && o != seat && Power(n) > 0)
                .Select(Power).DefaultIfEmpty(0).Max() - Power(h)))
            .Where(x => x.Gap >= 0).OrderByDescending(x => x.Gap).ThenBy(x => x.Tile.Q).ThenBy(x => x.Tile.R).ToList();
        var defend = threats.Count > 0 && threats[0].Gap > 0
            ? Orders.Summon(seat, threats[0].Tile, UnitKind.Imp, threats[0].Gap + 1)
            : null;

        var free = mine.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null && !match.Map[h].IsGuarded).ToList();
        var attune = free.Count == 0 ? null : Orders.Attune(seat, PickGood(free, h => Worth(match, h), random));
        var richFree = free.Where(h => Worth(match, h) >= 2).ToList();
        var richAttune = richFree.Count == 0 ? null
            : Orders.Attune(seat, richFree.OrderByDescending(h => Worth(match, h)).ThenBy(h => h.Q).ThenBy(h => h.R).First());

        // Every plan takes land while it's cheap (a small realm's price): no real player passes up a tile for a few mana.
        var cheap = attune is not null && rules.AttunePrice(me.Tiles.Count) <= rules.AttunePrice(rules.AttuneCheapTiles) ? attune : null;

        var border = mine.Where(h => match.Map.NeighboursOf(h).Any(n => match.OwnerOf(n) != seat)).ToList();
        var at = border.FirstOrDefault(mine[0]);
        var army = Orders.Summon(seat, at, UnitKind.Imp, Math.Max(1, me.Mana * 3 / 4));
        var some = Orders.Summon(seat, at, UnitKind.Imp, Math.Max(1, me.Mana / 2));

        var open = mine.Where(h => match.UpgradeOn(h) is null).ToList();
        Orders? Build(Upgrade first)
        {
            if (open.Count == 0 || match.UpgradesOf(me) >= rules.UpgradeCap)
            {
                return null;
            }

            var threatened = defend?.Target is { } d && open.Contains(d) ? d : (Hex?)null;
            return threatened is { } tower
                ? Orders.Build(seat, tower, Upgrade.Tower)
                : Orders.Build(seat, open.OrderByDescending(h => Worth(match, h)).ThenBy(h => h.Q).ThenBy(h => h.R).First(), first);
        }

        // A spell: its best purposeful cast for a Warlord, an Economist or a Spellcaster; any of them for the others,
        // which cast only when they have nothing better to do.
        var casts = Casts(match, seat, plan).ToList();
        var warlordCast = casts.FirstOrDefault(c => c.Spell is Spell.Scorch or Spell.Lure);
        var sanctum = mine.Any(h => match.UpgradeOn(h) == Upgrade.Sanctum) ? null : Build(Upgrade.Sanctum);

        IEnumerable<Orders?> order = plan switch
        {
            // A Warlord grows its army until it has a stack worth sending (8 power), then takes land as well; it scorches
            // or lures for its next attack.
            Plan.Warlord => mine.Max(Power) >= WarlordStack ? [attack, defend, warlordCast, attune, army] : [attack, defend, warlordCast, cheap, army, attune],
            Plan.Builder => [defend, cheap, match.CurrentRound >= 2 ? Build(Upgrade.Shrine) : null, attune, monsterAttack, .. casts, some],
            // A Spellcaster builds one Sanctum (from round 2), then casts every round it has a spell worth casting.
            Plan.Spellcaster => [attack, defend, cheap, match.CurrentRound >= 2 ? sanctum : null, .. casts, attune, some],
            Plan.Expander => [defend, attune, attack, .. casts, some],
            // An Economist grows income: a Shrine on its best tile (from round 2), rich land first, monsters it can beat.
            Plan.Economist => [defend, cheap, match.CurrentRound >= 2 ? Build(Upgrade.Shrine) : null, richAttune, monsterAttack, .. casts, attune, some],
            _ => [],
        };
        return order.OfType<Orders>();
    }

    /// <summary>The stack a Warlord builds before it also attunes.</summary>
    private const int WarlordStack = 8;

    /// <summary>The weakest target it can beat, sending everything from one tile; null if none.</summary>
    private static Orders? BestAttack(Match match, int seat, Seat me)
    {
        var rules = match.Rules;
        return (from origin in me.Tiles.OrderBy(h => h.Q).ThenBy(h => h.R)
                let army = match.UnitsOn(origin)
                where !army.IsEmpty
                from target in match.Map.NeighboursOf(origin)
                let owner = match.OwnerOf(target)
                where owner != seat && (owner is not null || match.Map[target].IsGuarded)
                let defence = owner is null ? match.Map[target].Guard : match.UnitsOn(target).Power(rules)
                where army.Power(rules) > defence
                orderby defence, target.Q, target.R
                select Orders.Attack(seat, target, [new Move(origin, target, army)])).FirstOrDefault();
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

        // A trait that scores spells casts one whenever it has one worth casting, once land is no longer cheap.
        var casts = Casts(match, seat, Plan.Balanced).ToList();
        if (me.Trait is Trait.Spellweaver or Trait.Hexer && rules.AttunePrice(me.Tiles.Count) > rules.AttunePrice(rules.AttuneCheapTiles))
        {
            foreach (var cast in casts)
            {
                yield return cast;
            }
        }

        var free = mine.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null && !match.Map[h].IsGuarded).ToList();

        // Attune: one of the best free tiles next to its land, leaning to its trait's.
        var attune = free.Count == 0 ? null
            : Orders.Attune(seat, PickGood(free, h => Worth(match, h) + (Suits(match, me, h) ? TraitLean : 0), random));

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

        // From round 3, with mana to spare and room for one, an upgrade: a Tower on a threatened border tile, a Sanctum
        // for a seat whose trait scores spells, else a Shrine on its best-yielding tile. It builds before attuning or
        // summoning. (Building from round 1 starved its land.)
        if (match.CurrentRound >= 3 && me.Mana >= rules.UpgradeCost + BuildSpare && match.UpgradesOf(me) < rules.UpgradeCap)
        {
            var open = mine.Where(h => match.UpgradeOn(h) is null).ToList();
            var threatened = open.FirstOrDefault(h => match.Map.NeighboursOf(h)
                .Any(n => match.OwnerOf(n) is { } o && o != seat && !match.UnitsOn(n).IsEmpty), new Hex(int.MinValue, 0));
            if (match.Map.Contains(threatened))
            {
                yield return Orders.Build(seat, threatened, Upgrade.Tower);
            }
            else if (open.Count > 0)
            {
                var best = open.OrderByDescending(h => Worth(match, h)).ThenBy(h => h.Q).ThenBy(h => h.R).First();
                yield return Orders.Build(seat, best, me.Trait is Trait.Spellweaver or Trait.Hexer ? Upgrade.Sanctum : Upgrade.Shrine);
            }
        }

        // Every other round it summons before it attunes, so it has units to fight with; but land comes first while it's
        // cheap. In the other rounds, with no land to attune, it casts a spell with a purpose before it summons.
        var unitsFirst = match.CurrentRound % 2 == 0 && rules.AttunePrice(me.Tiles.Count) > rules.AttunePrice(rules.AttuneCheapTiles);
        IEnumerable<Orders?> rest = unitsFirst ? [.. summons, attune] : [attune, .. casts, .. summons];
        foreach (var orders in rest)
        {
            if (orders is not null)
            {
                yield return orders;
            }
        }
    }
}
