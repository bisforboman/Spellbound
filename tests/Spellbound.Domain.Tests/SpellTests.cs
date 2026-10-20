using static Spellbound.Domain.Tests.Scenarios;

namespace Spellbound.Domain.Tests;

public class SpellTests
{
    private static Match Bordering(GameRules? rules = null) => FirstWhere(m => Border(m, 0, 1) is not null, rules);

    [Fact]
    public void Scorch_burns_power_weakest_first_for_two_mana_each()
    {
        var match = Bordering();
        var (_, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(1, theirs, UnitKind.Imp, 2));
        Play(match, Orders.Summon(1, theirs, UnitKind.Golem, 1));
        var mana = match.Seats[0].Mana;

        var check = match.Check(Orders.Cast(0, Spell.Scorch, theirs, 3));
        var report = Play(match, Orders.Cast(0, Spell.Scorch, theirs, 3));

        Assert.Contains("for 6 mana", Assert.Single(check.Lines), StringComparison.Ordinal);
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
        var match = FirstWhere(m => m.Seats[0].Tiles.Any(a => m.Seats[0].Tiles.Any(b => a != b && !a.IsNextTo(b))));
        var tiles = match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var (from, to) = tiles.SelectMany(a => tiles.Where(b => a != b && !a.IsNextTo(b)).Select(b => (a, b))).First();
        Play(match, Orders.Summon(0, from, UnitKind.Imp, 3));

        var report = Play(match, Orders.CastLeyline(0, new Move(from, to, new Army(2, 0))));

        Assert.Equal(new Army(1, 0), match.UnitsOn(from));
        Assert.Equal(new Army(2, 0), match.UnitsOn(to));
        Assert.Contains(report.Lines, l => l.Contains("casts Leyline", StringComparison.Ordinal));
        // A unit that steps this round can't also ride the leyline (when the layout has a neighbouring tile to step to).
        if (tiles.Where(from.IsNextTo).Cast<Hex?>().FirstOrDefault() is { } next)
        {
            var stepped = new Move(from, next, new Army(1, 0));
            Assert.False(match.Check(Orders.CastLeyline(0, new Move(from, to, new Army(1, 0)), stepped)).Succeeded);
        }
    }

    [Fact]
    public void Blight_leaves_a_rivals_tile_out_of_their_next_income()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is { } b && m.Map[b.Theirs].Yield > 0);
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var income = match.Seats[1].Tiles.Sum(h => match.Map[h].Yield!.Value);
        var before = match.Seats[1].Mana;

        var report = Play(match, Orders.Cast(0, Spell.Blight, theirs));

        Assert.Equal(before + income - match.Map[theirs].Yield!.Value, match.Seats[1].Mana);
        Assert.Contains(report.Lines, l => l.Contains($"Seat 2 collects {income - match.Map[theirs].Yield} mana", StringComparison.Ordinal));
        Play(match);
        Assert.Equal(before + 2 * income - match.Map[theirs].Yield!.Value, match.Seats[1].Mana); // one round only
    }

    [Fact]
    public void Lure_weakens_monster_guards_for_good_but_not_below_one()
    {
        var rules = GameRules.Default with { GuardedFaces = 6 };
        Hex? Guarded(Match m) => m.Map.Tiles.Where(t => t.IsGuarded && m.Seats[0].Tiles.Any(t.Hex.IsNextTo))
            .Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).Cast<Hex?>().FirstOrDefault();
        var match = FirstWhere(m => { Play(m); return Guarded(m) is not null; }, rules);
        Play(match);
        var monster = Guarded(match)!.Value;

        Play(match, Orders.Cast(0, Spell.Lure, monster));
        Assert.Equal(2, match.Map[monster].Guard);
        Play(match, Orders.Cast(0, Spell.Lure, monster));
        Assert.Equal(1, match.Map[monster].Guard);

        Assert.Contains("can't be weakened", Assert.Single(match.Check(Orders.Cast(0, Spell.Lure, monster)).Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attack_goes_with_the_units_a_rivals_Scorch_left()
    {
        var match = FirstWhere(m => m.Seats[0].Tiles.Any(a => m.Seats[1].Tiles.Any(a.IsNextTo) && m.Seats[2].Tiles.Any(a.IsNextTo)));
        var from = match.Seats[0].Tiles.First(a => match.Seats[1].Tiles.Any(a.IsNextTo) && match.Seats[2].Tiles.Any(a.IsNextTo));
        var target = match.Seats[1].Tiles.First(from.IsNextTo);
        Play(match, Orders.Summon(0, from, UnitKind.Imp, 4));

        var report = Play(match, Orders.Attack(0, target, [new Move(from, target, new Army(4, 0))]), Orders.Cast(2, Spell.Scorch, from, 4));

        Assert.Contains(report.Lines, l => l.Contains("has no units left to send", StringComparison.Ordinal));
        Assert.Equal(1, match.OwnerOf(target));
        Assert.Equal(Army.None, match.UnitsOn(from));
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
            "leyline to the same tile" => Orders.CastLeyline(0, new Move(mine, mine, new Army(1, 0))),
            _ => Orders.Cast(0, Spell.Ward, mine),
        };
        var before = Snapshot(match);

        var report = match.Resolve([orders, .. match.Seats.Where(s => s.Index != orders.Seat).Select(s => Orders.Hold(s.Index))], new SeededRandom(1));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }
}
