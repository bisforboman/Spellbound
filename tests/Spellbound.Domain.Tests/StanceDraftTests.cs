using System.Text.Json;

namespace Spellbound.Domain.Tests;

/// <summary>The rounds as a stance draft (docs/game-design.md, Rounds and stances): picks in turn, everyone takes each
/// stance, the picker a little cheaper.</summary>
public class StanceDraftTests
{
    /// <summary>A 3-player match at the start of round 1, with the default rules (the picker's discount included).</summary>
    private static Match Opened(GameRules? rules = null)
    {
        var random = new SeededRandom(1);
        var match = Match.Start(3, rules ?? GameRules.Default with { StartingMana = 20 }, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(new Pick(match.Picker!.Value, match.ToPick[0]), random);
        }

        return match;
    }

    private static Hex Home(Match match, int seat) => match.Seats[seat].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).First();

    private static Report Take(Match match, Orders orders)
    {
        var report = match.Take(orders, new SeededRandom(3));
        Assert.True(report.Succeeded, string.Join('\n', report.Lines));
        return report;
    }

    private static string Saved(Match match) => JsonSerializer.Serialize(match.Save());

    [Fact]
    public void A_picked_stance_goes_round_the_table_from_its_picker_then_the_next_seat_picks()
    {
        var match = Opened();

        Assert.Equal(0, match.Actor);
        Assert.True(match.IsPicking(0));
        Take(match, Orders.Summon(0, Home(match, 0), UnitKind.Imp, 1));
        Assert.Equal(Stance.Summon, match.OpenStance);
        Assert.Equal(1, match.Actor);
        Take(match, Orders.Summon(1, Home(match, 1), UnitKind.Imp, 1));
        Take(match, Orders.Pass(2, Stance.Summon));

        Assert.Null(match.OpenStance);
        Assert.Equal(1, match.Actor);
        Assert.True(match.IsPicking(1));
        Assert.DoesNotContain(Stance.Summon, match.Unpicked);
        Assert.Equal(new Army(1, 0), match.UnitsOn(Home(match, 1)));
    }

    [Fact]
    public void The_first_picker_moves_one_seat_each_round()
    {
        var match = Opened();
        Scenarios.Play(match);

        Assert.Equal(2, match.CurrentRound);
        Assert.Equal(1, match.FirstPicker);
        Assert.Equal(1, match.Actor);
        Assert.Empty(match.Picks);
    }

    [Fact]
    public void The_picker_pays_one_mana_less_and_the_seats_that_follow_pay_full_price()
    {
        var match = Opened();
        var (picker, follower) = (match.Seats[0].Mana, match.Seats[1].Mana);

        var check = match.Check(Orders.Summon(0, Home(match, 0), UnitKind.Imp, 3));
        Take(match, Orders.Summon(0, Home(match, 0), UnitKind.Imp, 3));
        Take(match, Orders.Summon(1, Home(match, 1), UnitKind.Imp, 3));

        Assert.Contains(check.Lines, l => l.Contains("you pay 1 less: 2 mana in all", StringComparison.Ordinal));
        Assert.Equal(picker - 2, match.Seats[0].Mana);
        Assert.Equal(follower - 3, match.Seats[1].Mana);
    }

    [Fact]
    public void A_pick_aimed_at_nothing_costs_nothing_and_keeps_the_discount_unused()
    {
        var match = Opened();
        var mana = match.Seats[0].Mana;

        var report = Take(match, Orders.Pass(0, Stance.Attack));

        Assert.Equal(mana, match.Seats[0].Mana);
        Assert.Contains("Seat 1 passes.", report.Lines);
        Assert.Equal(Stance.Attack, match.OpenStance); // the others may still attack
    }

    [Fact]
    public void Channel_gives_its_picker_mana_and_nobody_follows()
    {
        var match = Opened();
        var mana = match.Seats[0].Mana;

        var report = Take(match, Orders.Channel(0));

        Assert.Equal(mana + match.Rules.ChannelMana, match.Seats[0].Mana);
        Assert.Contains(report.Lines, l => l == $"Seat 1 channels {match.Rules.ChannelMana} mana.");
        Assert.Null(match.OpenStance);
        Assert.True(match.IsPicking(1));
    }

    public static TheoryData<string, string> BadTurns => new()
    {
        { "out of turn", "It's Seat 1's turn, not Seat 2's" },
        { "picked twice", "has already picked Summon" },
        { "another stance while one goes round", "picked Summon: take it too, or pass" },
        { "moves while following", "Units move only with your own pick" },
    };

    [Theory]
    [MemberData(nameof(BadTurns))]
    public void A_turn_that_doesnt_fit_changes_nothing(string problem, string reason)
    {
        var match = Opened();
        if (problem != "out of turn")
        {
            Take(match, Orders.Summon(0, Home(match, 0), UnitKind.Imp, 1));
        }

        if (problem == "picked twice")
        {
            Take(match, Orders.Pass(1, Stance.Summon));
            Take(match, Orders.Pass(2, Stance.Summon));
        }

        var mine = match.Seats[1].Tiles.OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var orders = problem switch
        {
            "out of turn" => Orders.Summon(1, mine[0], UnitKind.Imp, 1),
            "picked twice" => Orders.Summon(1, mine[0], UnitKind.Imp, 1),
            "another stance while one goes round" => Orders.Pass(1, Stance.Attack),
            _ => Orders.Pass(1, Stance.Summon, new Move(mine[0], mine[1], new Army(1, 0))),
        };
        var before = Saved(match);

        var report = match.Take(orders, new SeededRandom(3));

        Assert.False(report.Succeeded);
        Assert.Contains(reason, report.Lines[0], StringComparison.Ordinal);
        Assert.Equal(before, Saved(match));
    }

    [Fact]
    public void Letting_a_turn_go_by_channels_on_a_pick_and_passes_on_a_follow()
    {
        var match = Opened();

        Assert.Equal(Orders.Channel(0), match.Passing(0));
        Take(match, Orders.Summon(0, Home(match, 0), UnitKind.Imp, 1));
        Assert.Equal(Orders.Pass(1, Stance.Summon), match.Passing(1));
    }

    [Fact]
    public void A_round_saved_between_turns_loads_and_plays_on_the_same()
    {
        var unbroken = Opened();
        var reloaded = Opened();
        Orders[] turns(Match m) => [Orders.Summon(0, Home(m, 0), UnitKind.Imp, 2), Orders.Pass(1, Stance.Summon), Orders.Pass(2, Stance.Summon), Orders.Channel(1)];

        foreach (var orders in turns(unbroken))
        {
            Take(unbroken, orders);
        }

        foreach (var orders in turns(reloaded))
        {
            Take(reloaded, orders);
            reloaded = Match.Load(reloaded.Save(), reloaded.Rules);
        }

        Assert.Equal(Saved(unbroken), Saved(reloaded));
        Assert.Equal(2, reloaded.Picks.Count);
        Assert.Equal(2, reloaded.Actor);
    }

    [Fact]
    public void Several_casts_in_one_round_count_as_one_round_of_casting()
    {
        var match = Opened(Scenarios.AnySpell(GameRules.Default with { StartingMana = 30 }));
        var mine = Home(match, 0);

        Take(match, Orders.Cast(0, Spell.Ward, mine));
        Take(match, Orders.Pass(1, Stance.Cast));
        Take(match, Orders.Pass(2, Stance.Cast));
        var ward = Assert.Single(match.Save().Wards!);

        Assert.Equal(mine, ward.Hex);
        Assert.Equal(1, match.Seats[0].RoundsCast);
    }
}
