using static Spellbound.Domain.Tests.Scenarios;

namespace Spellbound.Domain.Tests;

public class SpellTests
{
    private static Match Bordering(GameRules? rules = null) => FirstWhere(m => Border(m, 0, 1) is not null, rules);

    [Fact]
    public void Scorch_burns_power_weakest_first_and_charges_for_what_burns()
    {
        var match = Bordering();
        var (_, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(1, theirs, UnitKind.Imp, 2));
        Play(match, Orders.Summon(1, theirs, UnitKind.Golem, 1));
        var mana = match.Seats[0].Mana;

        var check = match.Check(Orders.Hold(0).Casting(Spell.Scorch, theirs, 3));
        var report = Play(match, Orders.Hold(0).Casting(Spell.Scorch, theirs, 3));

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

        var report = Play(match, Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(4, 0))]), Orders.Hold(1).Casting(Spell.Ward, theirs));

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

        var report = Play(match, Orders.Hold(0).CastingLeyline(new Move(from, to, new Army(2, 0))));

        Assert.Equal(new Army(1, 0), match.UnitsOn(from));
        Assert.Equal(new Army(2, 0), match.UnitsOn(to));
        Assert.Contains(report.Lines, l => l.Contains("casts Leyline", StringComparison.Ordinal));
        // A unit that steps this round can't also ride the leyline (when the layout has a neighbouring tile to step to).
        if (tiles.Where(from.IsNextTo).Cast<Hex?>().FirstOrDefault() is { } next)
        {
            var stepped = new Move(from, next, new Army(1, 0));
            Assert.False(match.Check(Orders.Hold(0, stepped).CastingLeyline(new Move(from, to, new Army(1, 0)))).Succeeded);
        }
    }

    [Fact]
    public void Blight_leaves_a_rivals_tile_out_of_their_next_income()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is { } b && m.Map[b.Theirs].Yield > 0);
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var income = match.Seats[1].Tiles.Sum(h => match.Map[h].Yield!.Value);
        var before = match.Seats[1].Mana;

        var report = Play(match, Orders.Hold(0).Casting(Spell.Blight, theirs));

        Assert.Equal(before + income - match.Map[theirs].Yield!.Value, match.Seats[1].Mana);
        Assert.Contains(report.Lines, l => l.Contains($"Seat 2 collects {income - match.Map[theirs].Yield} mana", StringComparison.Ordinal));
        Play(match);
        Assert.Equal(before + 2 * income - match.Map[theirs].Yield!.Value, match.Seats[1].Mana); // one round only
    }

    [Fact]
    public void A_tile_cant_be_blighted_two_rounds_running_even_after_a_save()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is { } b && m.Map[b.Theirs].Yield > 0);
        var (_, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Hold(0).Casting(Spell.Blight, theirs));
        match = Match.Load(match.Save(), match.Rules);
        var again = Orders.Hold(0).Casting(Spell.Blight, theirs);
        var before = System.Text.Json.JsonSerializer.Serialize(match.Save());

        var check = match.Check(again);
        var report = match.Resolve([again, Orders.Hold(1), Orders.Hold(2)], new SeededRandom(1));

        Assert.False(check.Succeeded);
        Assert.Contains("can't be blighted two rounds running", check.Lines[0], StringComparison.Ordinal);
        Assert.False(report.Succeeded);
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
        var match = FirstWhere(m => { Play(m); return Guarded(m) is not null; }, rules);
        Play(match);
        var monster = Guarded(match)!.Value;

        Play(match, Orders.Hold(0).Casting(Spell.Lure, monster));
        Assert.Equal(2, match.Map[monster].Guard);
        Play(match, Orders.Hold(0).Casting(Spell.Lure, monster));
        Assert.Equal(1, match.Map[monster].Guard);

        Assert.Contains("can't be weakened", Assert.Single(match.Check(Orders.Hold(0).Casting(Spell.Lure, monster)).Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attack_goes_with_the_units_a_rivals_Scorch_left()
    {
        var match = FirstWhere(m => m.Seats[0].Tiles.Any(a => m.Seats[1].Tiles.Any(a.IsNextTo) && m.Seats[2].Tiles.Any(a.IsNextTo)));
        var from = match.Seats[0].Tiles.First(a => match.Seats[1].Tiles.Any(a.IsNextTo) && match.Seats[2].Tiles.Any(a.IsNextTo));
        var target = match.Seats[1].Tiles.First(from.IsNextTo);
        Play(match, Orders.Summon(0, from, UnitKind.Imp, 4));

        var report = Play(match, Orders.Attack(0, target, [new Move(from, target, new Army(4, 0))]), Orders.Hold(2).Casting(Spell.Scorch, from, 4));

        Assert.Contains(report.Lines, l => l.Contains("has no units left to send", StringComparison.Ordinal));
        Assert.Equal(1, match.OwnerOf(target));
        Assert.Equal(Army.None, match.UnitsOn(from));
    }

    [Fact]
    public void A_spell_is_cast_alongside_the_stance_and_both_are_paid()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is not null
            && m.Seats[0].Tiles.SelectMany(m.Map.NeighboursOf).Any(h => m.Map[h].IsUnveiled && m.OwnerOf(h) is null && !m.Map[h].IsGuarded));
        var (mine, _) = Border(match, 0, 1)!.Value;
        var free = match.Seats[0].Tiles.SelectMany(match.Map.NeighboursOf)
            .First(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null && !match.Map[h].IsGuarded);
        var orders = Orders.Attune(0, free).Casting(Spell.Ward, mine);

        var check = match.Check(orders);
        Play(match, orders);

        Assert.Contains(check.Lines, l => l.StartsWith("Cast Ward for 3 mana", StringComparison.Ordinal));
        Assert.Contains(check.Lines, l => l.StartsWith("All together: 7 mana", StringComparison.Ordinal)); // Attune 4 + Ward 3
        Assert.Equal(0, match.OwnerOf(free));
    }

    [Fact]
    public void A_stance_and_spell_too_dear_together_change_nothing()
    {
        var match = Bordering();
        var (mine, _) = Border(match, 0, 1)!.Value;
        var all = match.Seats[0].Mana;
        var before = Snapshot(match);

        var report = match.Resolve(
            [Orders.Summon(0, mine, UnitKind.Imp, all).Casting(Spell.Ward, mine), Orders.Hold(1), Orders.Hold(2)], new SeededRandom(1));

        Assert.Contains("cost " + (all + 3) + " mana together", Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Scorch_softens_a_defence_before_your_own_attack_in_the_same_round()
    {
        var match = Bordering();
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 3), Orders.Summon(1, theirs, UnitKind.Golem, 1));

        // 3 Imps against a Golem would tie; Scorch can't burn part of a Golem, so it burns all 3 power first.
        Play(match, Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(3, 0))]).Casting(Spell.Scorch, theirs, 3));

        Assert.Equal(0, match.OwnerOf(theirs));
        Assert.Equal(new Army(3, 0), match.UnitsOn(theirs));
    }

    [Fact]
    public void Scorch_charges_nothing_for_units_that_moved_away_first()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is { } b && m.Seats[1].Tiles.Any(t => t != b.Theirs && t.IsNextTo(b.Theirs)));
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var away = match.Seats[1].Tiles.First(t => t != theirs && t.IsNextTo(theirs));
        Play(match, Orders.Summon(1, theirs, UnitKind.Imp, 3));
        var mana = match.Seats[0].Mana;

        // Moves come before spells: the Imps leave, the Scorch finds nothing and costs nothing.
        var report = Play(match, Orders.Hold(0).Casting(Spell.Scorch, theirs, 3), Orders.Hold(1, new Move(theirs, away, new Army(3, 0))));

        Assert.Equal(new Army(3, 0), match.UnitsOn(away));
        Assert.Contains(report.Lines, l => l.Contains("casts Scorch", StringComparison.Ordinal) && l.Contains("for 0 mana", StringComparison.Ordinal));
        Assert.Equal(mana, match.Seats[0].Mana - match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value));
    }

    [Fact]
    public void A_Scorch_that_would_burn_nothing_is_refused_and_one_that_burns_nothing_doesnt_count_for_traits()
    {
        var match = FirstWhere(m => Border(m, 0, 1) is { } b && m.Seats[1].Tiles.Any(t => t != b.Theirs && t.IsNextTo(b.Theirs)));
        var (_, theirs) = Border(match, 0, 1)!.Value;
        var away = match.Seats[1].Tiles.First(t => t != theirs && t.IsNextTo(theirs));
        Play(match, Orders.Summon(1, theirs, UnitKind.Golem, 1));
        var before = Snapshot(match);

        // 1 power can't burn a Golem of power 3: refused, changing nothing.
        var tooSmall = match.Check(Orders.Hold(0).Casting(Spell.Scorch, theirs, 1));
        Assert.False(tooSmall.Succeeded);
        Assert.Contains("would burn nothing", tooSmall.Lines[0], StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));

        // A Scorch whose Golem walks away burns nothing, costs nothing, and isn't a cast for Hexer or Spellweaver.
        Play(match, Orders.Hold(0).Casting(Spell.Scorch, theirs, 3), Orders.Hold(1, new Move(theirs, away, new Army(0, 1))));
        Assert.Equal(0, match.Seats[0].HexesCast);
        Assert.Empty(match.Seats[0].SpellsCast);
    }

    [Fact]
    public void An_attacks_preview_counts_your_own_Scorch()
    {
        var match = Bordering();
        var (mine, theirs) = Border(match, 0, 1)!.Value;
        Play(match, Orders.Summon(0, mine, UnitKind.Imp, 3), Orders.Summon(1, theirs, UnitKind.Golem, 1));

        var plain = match.Check(Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(3, 0))]));
        var scorched = match.Check(Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(3, 0))]).Casting(Spell.Scorch, theirs, 3));

        Assert.Contains("a tie", plain.Lines[0], StringComparison.Ordinal);
        Assert.Contains("power 0 after your Scorch", scorched.Lines[^2], StringComparison.Ordinal);
        Assert.Contains("you win and take it", scorched.Lines[^2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_tile_attuned_this_round_can_be_warded()
    {
        var match = FirstWhere(m => FreeNextTo(m, 0) is not null);
        var free = FreeNextTo(match, 0)!.Value;

        Assert.False(match.Check(Orders.Hold(0).Casting(Spell.Ward, free)).Succeeded);
        var check = match.Check(Orders.Attune(0, free).Casting(Spell.Ward, free));
        Play(match, Orders.Attune(0, free).Casting(Spell.Ward, free));

        Assert.True(check.Succeeded, string.Join('\n', check.Lines));
        Assert.Contains(check.Lines, l => l.Contains("if your Attune takes it", StringComparison.Ordinal));
        Assert.Equal(0, match.OwnerOf(free));
    }

    [Fact]
    public void Units_summoned_this_round_can_ride_a_Leyline()
    {
        var match = FirstWhere(m => m.Seats[0].Tiles.Any(a => m.Seats[0].Tiles.Any(b => a != b && !a.IsNextTo(b))));
        var tiles = match.Seats[0].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var (from, to) = tiles.SelectMany(a => tiles.Where(b => a != b && !a.IsNextTo(b)).Select(b => (a, b))).First();

        Play(match, Orders.Summon(0, from, UnitKind.Imp, 3).CastingLeyline(new Move(from, to, new Army(2, 0))));

        Assert.Equal(new Army(1, 0), match.UnitsOn(from));
        Assert.Equal(new Army(2, 0), match.UnitsOn(to));
    }

    private static Hex? FreeNextTo(Match match, int seat) =>
        match.Seats[seat].Tiles.SelectMany(match.Map.NeighboursOf).Distinct().OrderBy(h => h.Q).ThenBy(h => h.R)
            .Where(h => match.Map[h].IsUnveiled && !match.Map[h].IsGuarded && match.OwnerOf(h) is null).Cast<Hex?>().FirstOrDefault();

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
            "scorch your own tile" => Orders.Hold(0).Casting(Spell.Scorch, mine, 1),
            "scorch more than is there" => Orders.Hold(0).Casting(Spell.Scorch, theirs, 3),
            "scorch nothing" => Orders.Hold(1).Casting(Spell.Scorch, mine, 1),
            "ward a rival's tile" => Orders.Hold(0).Casting(Spell.Ward, theirs),
            "blight your own tile" => Orders.Hold(0).Casting(Spell.Blight, mine),
            "lure a tile without monsters" => Orders.Hold(0).Casting(Spell.Lure, theirs),
            "leyline to the same tile" => Orders.Hold(0).CastingLeyline(new Move(mine, mine, new Army(1, 0))),
            _ => Orders.Hold(0).Casting(Spell.Ward, mine),
        };
        var before = Snapshot(match);

        var report = match.Resolve([orders, .. match.Seats.Where(s => s.Index != orders.Seat).Select(s => Orders.Hold(s.Index))], new SeededRandom(1));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, Assert.Single(report.Lines), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(match));
    }
}
