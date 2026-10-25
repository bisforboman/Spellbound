using static Spellbound.Domain.Tests.Scenarios;

namespace Spellbound.Domain.Tests;

public class SpellTests
{
    private static Match Bordering(GameRules? rules = null) => Where(m => Border(m, 0, 1) is not null, rules);

    /// <summary>The first seeded match where the condition holds, with every trait able to cast every spell.</summary>
    private static Match Where(Func<Match, bool> condition, GameRules? rules = null) => FirstWhere(condition, AnySpell(rules));

    [Fact]
    public void Scorch_burns_power_weakest_first_and_charges_for_what_burns()
    {
        var match = Bordering();
        var (_, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(1, theirs, UnitKind.Imp, 2));
        Play(match, Orders.Summon(1, theirs, UnitKind.Golem, 1));
        var mana = match.Seats[0].Mana;

        var check = match.Check(Orders.Cast(0, Spell.Scorch, theirs, 3));
        var report = Play(match, Orders.Cast(0, Spell.Scorch, theirs, 3));

        Assert.Contains(check.Lines, l => l.StartsWith("Cast Scorch for 6 mana", StringComparison.Ordinal));
        Assert.Equal(new Army(0, 1), match.UnitsOn(theirs)); // 2 Imps burn first, then the Golem would go past 3
        Assert.Contains(report.Lines, l => l.Contains("casts Scorch", StringComparison.Ordinal));
        Assert.Equal(mana - 6, match.Seats[0].Mana - match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value));
    }

    [Fact]
    public void Ward_turns_a_winning_attack_into_a_loss_and_takes_the_blows_first()
    {
        var match = Bordering();
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 4), Orders.Summon(1, theirs, UnitKind.Golem, 1));

        var report = Play(match, Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(4, 0))]), Orders.Cast(1, Spell.Ward, theirs));

        Assert.Contains(report.Lines, l => l.Contains("power 4 against 6", StringComparison.Ordinal));
        Assert.Equal(1, match.OwnerOf(theirs));
        Assert.Equal(new Army(0, 1), match.UnitsOn(theirs)); // the Ward took 3 of the 4: the Golem lives
    }

    [Fact]
    public void Leyline_moves_units_across_a_gap_and_not_units_that_already_stepped()
    {
        var match = Where(m => m.Seats[0].Tiles.Any(a => m.Seats[0].Tiles.Any(b => a != b && !a.IsNextTo(b))));
        var tiles = match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var (from, to) = tiles.SelectMany(a => tiles.Where(b => a != b && !a.IsNextTo(b)).Select(b => (a, b))).First();
        Play(match, Orders.Summon(0, from, UnitKind.Imp, 3));

        var report = Play(match, Orders.Leyline(0, new Move(from, to, new Army(2, 0))));

        Assert.Equal(new Army(1, 0), match.UnitsOn(from));
        Assert.Equal(new Army(2, 0), match.UnitsOn(to));
        Assert.Contains(report.Lines, l => l.Contains("casts Leyline", StringComparison.Ordinal));
        // A unit that steps this round can't also ride the leyline (when the layout has a neighbouring tile to step to).
        if (tiles.Where(from.IsNextTo).Cast<Hex?>().FirstOrDefault() is { } next)
        {
            var stepped = new Move(from, next, new Army(1, 0));
            Assert.False(match.Check(Orders.Leyline(0, new Move(from, to, new Army(1, 0)), stepped)).Succeeded);
        }
    }

    [Fact]
    public void Blight_leaves_a_rivals_tile_out_of_their_next_income()
    {
        var match = Where(m => Border(m, 0, 1) is { } b && m.Map[b.Theirs].Type == TileType.Steady);
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var before = match.Seats[1].Mana;

        var report = Play(match, Orders.Cast(0, Spell.Blight, theirs));

        // Each tile's yield is the roll this round's income used.
        var income = match.Seats[1].Tiles.Where(h => h != theirs).Sum(h => match.Map[h].Yield!.Value);
        Assert.Equal(before + income, match.Seats[1].Mana);
        Assert.Contains(report.Lines, l => l.Contains($"Seat 2 collects {income} mana", StringComparison.Ordinal));
        Assert.Equal(0, match.IncomeOf(theirs)); // what the page shows on the tile
        Assert.All(match.Seats, seat => Assert.Equal(seat.ProducedThisRound, seat.Tiles.Sum(match.IncomeOf)));
        before = match.Seats[1].Mana;
        Play(match);
        Assert.Equal(before + match.Seats[1].Tiles.Sum(h => match.Map[h].Yield!.Value), match.Seats[1].Mana); // one round only
    }

    [Fact]
    public void A_tile_cant_be_blighted_two_rounds_running_even_after_a_save()
    {
        var match = Where(m => Border(m, 0, 1) is { } b && m.Map[b.Theirs].Yield > 0);
        var (_, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Cast(0, Spell.Blight, theirs));
        match = Match.Load(match.Save(), match.Rules);
        var again = Orders.Cast(0, Spell.Blight, theirs);
        var before = System.Text.Json.JsonSerializer.Serialize(match.Save());

        var check = match.Check(again);

        Assert.False(check.Succeeded);
        Assert.Contains("can't be blighted two rounds running", check.Lines[0], StringComparison.Ordinal);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(match.Save()));

        Play(match);
        Assert.True(match.Check(again).Succeeded); // a round later it can be blighted again
    }

    [Fact]
    public void Lure_weakens_monster_guards_for_good_but_not_below_one()
    {
        var rules = GameRules.Default with { GuardedFaces = 6 };
        Hex? Guarded(Match m) => m.Map.Tiles.Where(t => t.IsGuarded && m.Seats[0].Tiles.Any(t.Hex.IsNextTo))
            .Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).Cast<Hex?>().FirstOrDefault();
        var match = Where(m => { Play(m); return Guarded(m) is not null; }, rules);
        Play(match);
        var monster = Guarded(match)!.Value;

        Play(match, Orders.Cast(0, Spell.Lure, monster));
        Assert.Equal(2, match.Map[monster].Guard);
        Play(match, Orders.Cast(0, Spell.Lure, monster));
        Assert.Equal(1, match.Map[monster].Guard);

        Assert.Contains("can't be weakened", Assert.Single(match.Check(Orders.Cast(0, Spell.Lure, monster)).Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Scorch_earlier_in_the_round_leaves_fewer_units_to_attack_with()
    {
        var match = Where(m => m.Seats[0].Tiles.Any(a => m.Seats[1].Tiles.Any(a.IsNextTo) && m.Seats[2].Tiles.Any(a.IsNextTo)));
        var from = match.Seats[0].Tiles.First(a => match.Seats[1].Tiles.Any(a.IsNextTo) && match.Seats[2].Tiles.Any(a.IsNextTo));
        var target = match.Seats[1].Tiles.First(from.IsNextTo);
        Play(match, Orders.Summon(0, from, UnitKind.Imp, 4));

        Play(match, Orders.Cast(2, Spell.Scorch, from, 3));

        Assert.Equal(new Army(1, 0), match.UnitsOn(from));
        Assert.Contains("There aren't 4 Imps", match.Check(Orders.Attack(0, target, [new Move(from, target, new Army(4, 0))])).Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_Cast_round_casts_one_spell_as_its_stance_and_pays_only_for_it()
    {
        var match = Bordering();
        var (mine, _) = Border(match, 0, 1)!.Value;
        var mana = match.Seats[0].Mana;
        var orders = Orders.Cast(0, Spell.Ward, mine);

        var check = match.Check(orders);
        var report = Play(match, orders);

        Assert.True(check.Succeeded, string.Join('\n', check.Lines));
        Assert.Contains(check.Lines, l => l.StartsWith($"Cast Ward for 3 mana, leaving {mana - 3}", StringComparison.Ordinal));
        Assert.Equal(3, match.CostOf(orders));
        Assert.Contains(report.Lines, l => l.Contains("Seat 1 casts Ward", StringComparison.Ordinal));
        Assert.Contains(Spell.Ward, match.Seats[0].SpellsCast);
        Assert.Equal(mana - 3, match.Seats[0].Mana - match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value));
    }

    [Fact]
    public void A_seat_casts_only_its_traits_spell()
    {
        // Default rules: a Hearthkeeper's spell is Ward, and nothing else.
        var match = FirstWhere(m => m.Seats[0].Trait == Trait.Hearthkeeper && Border(m, 0, 1) is not null);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        var before = Snapshot(match);

        var blight = match.Resolve([Orders.Cast(0, Spell.Blight, theirs), Scenarios.Hold(1), Scenarios.Hold(2)], new SeededRandom(1));

        Assert.Equal([Spell.Ward], match.SpellsOf(0));
        Assert.False(blight.Succeeded);
        Assert.Contains("Blight isn't one of your spells; yours: Ward", Assert.Single(blight.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
        Assert.True(match.Check(Orders.Cast(0, Spell.Ward, mine)).Succeeded);
    }

    [Fact]
    public void A_Ward_goes_on_several_tiles_each_paid_and_counts_as_one_cast()
    {
        var match = Bordering();
        var tiles = match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).Take(2).ToList();
        var mana = match.Seats[0].Mana;
        var orders = Orders.CastOn(0, Spell.Ward, tiles);

        var check = match.Check(orders);
        var report = Play(match, orders);

        Assert.Contains(check.Lines, l => l.StartsWith($"All together: Ward on 2 tiles for 6 mana, leaving {mana - 6}", StringComparison.Ordinal));
        Assert.Equal(6, match.CostOf(orders));
        Assert.Equal(2, report.Lines.Count(l => l.Contains("Seat 1 casts Ward", StringComparison.Ordinal)));
        Assert.Equal(1, match.Seats[0].RoundsCast); // one Cast, however many tiles
        Assert.Equal(mana - 6, match.Seats[0].Mana - match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value));
    }

    public static TheoryData<string, string> BadMultiCasts => new()
    {
        { "scorch on two tiles", "Only Ward, Blight and Lure go on several tiles" },
        { "the same tile twice", "Each tile takes Ward once" },
        { "one tile not yours", "Ward needs one of your tiles" },
        { "too dear together", "Ward on 2 tiles costs 8 mana; you have" },
    };

    [Theory]
    [MemberData(nameof(BadMultiCasts))]
    public void A_spell_on_several_tiles_that_doesnt_check_changes_nothing(string problem, string reason)
    {
        // "too dear together": one Ward fits seat 0's mana after the first round, two don't.
        static void Setup(Match m) => Play(m, Orders.Summon(1, Border(m, 0, 1)!.Value.Theirs, UnitKind.Imp, 2));
        var match = problem == "too dear together"
            ? Where(m => Border(m, 0, 1) is not null && Mana(m) is >= 4 and < 8, GameRules.Default with { WardCost = 4, StartingMana = 2 })
            : Bordering();
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        var other = match.Seats[0].Tiles.First(h => h != mine);
        Setup(match);
        var orders = problem switch
        {
            "scorch on two tiles" => Orders.Cast(0, Spell.Scorch, theirs, 1) with { MoreTiles = [mine] },
            "the same tile twice" => Orders.CastOn(0, Spell.Ward, [mine, mine]),
            "one tile not yours" => Orders.CastOn(0, Spell.Ward, [mine, theirs]),
            _ => Orders.CastOn(0, Spell.Ward, [mine, other]),
        };
        var before = Snapshot(match);

        var report = match.Resolve([orders, Scenarios.Hold(1), Scenarios.Hold(2)], new SeededRandom(1));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));

        static int Mana(Match m)
        {
            Setup(m);
            return m.Seats[0].Mana;
        }
    }

    [Fact]
    public void A_Cast_with_no_spell_to_afford_changes_nothing()
    {
        var match = Bordering(GameRules.Default with { BlightCost = 1000 });
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var before = Snapshot(match);

        var report = match.Resolve([Orders.Cast(0, Spell.Blight, theirs), Scenarios.Hold(1), Scenarios.Hold(2)], new SeededRandom(1));

        Assert.False(report.Succeeded);
        Assert.Contains("Blight costs 1000 mana", Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Units_that_moved_away_before_a_Scorch_are_out_of_its_reach()
    {
        var match = Where(m => Border(m, 0, 1) is { } b && m.Seats[1].Tiles.Any(t => t != b.Theirs && t.IsNextTo(b.Theirs)));
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var away = match.Seats[1].Tiles.First(t => t != theirs && t.IsNextTo(theirs));
        Play(match, Orders.Summon(1, theirs, UnitKind.Imp, 3));

        // Round 2: Seat 2 picks first and moves its Imps away; Seat 1's Scorch then finds nothing to burn.
        Play(match, Scenarios.Hold(1, new Move(theirs, away, new Army(3, 0))));

        Assert.Equal(new Army(3, 0), match.UnitsOn(away));
        Assert.Contains("nothing at", match.Check(Orders.Cast(0, Spell.Scorch, theirs, 3)).Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_Scorch_that_would_burn_nothing_is_refused()
    {
        var match = Where(m => Border(m, 0, 1) is { } b && m.Seats[1].Tiles.Any(t => t != b.Theirs && t.IsNextTo(b.Theirs)));
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var away = match.Seats[1].Tiles.First(t => t != theirs && t.IsNextTo(theirs));
        Play(match, Orders.Summon(1, theirs, UnitKind.Golem, 1));
        var before = Snapshot(match);

        // 1 power can't burn a Golem of power 3: refused, changing nothing.
        var tooSmall = match.Check(Orders.Cast(0, Spell.Scorch, theirs, 1));
        Assert.False(tooSmall.Succeeded);
        Assert.Contains("would burn nothing", tooSmall.Lines[0], StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
        Assert.Equal(0, match.Seats[0].HexesCast);
        Assert.Empty(match.Seats[0].SpellsCast);
    }

    public static TheoryData<string, string> BadCasts => new()
    {
        { "scorch your own tile", "rival's tile next to yours" },
        { "scorch more than is there", "can burn from 1 to" },
        { "scorch nothing", "nothing at" },
        { "ward a rival's tile", "one of your tiles" },
        { "blight your own tile", "rival's tile next to yours" },
        { "lure a tile without monsters", "monster tile next to yours" },
        { "leyline to the same tile", "two different tiles" },
        { "rally, no longer a spell", "Rally is no longer a spell" },
        { "too dear", "Ward costs 1000 mana; you have" },
    };

    [Theory]
    [MemberData(nameof(BadCasts))]
    public void A_spell_that_doesnt_check_changes_nothing(string problem, string reason)
    {
        // The opening doesn't depend on spell costs, so "too dear" plays the same match with Ward out of reach.
        var match = Bordering(problem == "too dear" ? GameRules.Default with { WardCost = 1000 } : null);
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(1, theirs, UnitKind.Imp, 2));

        var orders = problem switch
        {
            "scorch your own tile" => Orders.Cast(0, Spell.Scorch, mine, 1),
            "scorch more than is there" => Orders.Cast(0, Spell.Scorch, theirs, 3),
            "scorch nothing" => Orders.Cast(1, Spell.Scorch, mine, 1),
            "ward a rival's tile" => Orders.Cast(0, Spell.Ward, theirs),
            "blight your own tile" => Orders.Cast(0, Spell.Blight, mine),
            "lure a tile without monsters" => Orders.Cast(0, Spell.Lure, theirs),
            "leyline to the same tile" => Orders.Leyline(0, new Move(mine, mine, new Army(1, 0))),
            "rally, no longer a spell" => Orders.Cast(0, Spell.Rally, theirs),
            _ => Orders.Cast(0, Spell.Ward, mine),
        };
        var before = Snapshot(match);

        var report = match.Resolve([orders, .. match.Seats.Where(s => s.Index != orders.Seat).Select(s => Scenarios.Hold(s.Index))], new SeededRandom(1));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }
}
