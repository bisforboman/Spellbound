namespace Spellbound.Domain.Tests;

public class FollowTests
{
    private static Match Opened(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        return match;
    }

    private static IEnumerable<Hex> FreeNextTo(Match match, int seat) =>
        match.Seats[seat].Tiles.SelectMany(match.Map.NeighboursOf).Distinct()
            .Where(h => match.Map[h].IsUnveiled && match.OwnerOf(h) is null && !match.Map[h].IsGuarded).OrderBy(h => h.Q).ThenBy(h => h.R);

    private static Report Play(Match match, params Orders[] orders) =>
        match.Resolve([.. orders, .. match.Seats.Where(s => orders.All(o => o.Seat != s.Index)).Select(s => Orders.Hold(s.Index))],
            new SeededRandom(match.CurrentRound));

    private static int Income(Match match, int seat) => match.Seats[seat].Tiles.Sum(h => match.Map[h].Yield!.Value);

    [Fact]
    public void A_follow_order_happens_when_a_rival_chooses_its_stance_and_costs_the_surcharge()
    {
        var match = Opened(1);
        var mine = match.Seats[0].Tiles.First();
        var mana = match.Seats[0].Mana;
        var orders = Orders.Hold(0).Following(Orders.Summon(0, mine, UnitKind.Imp, 2));

        var check = match.Check(orders);
        var report = Play(match, orders, Orders.Summon(1, match.Seats[1].Tiles.First(), UnitKind.Imp, 1));

        Assert.Contains(check.Lines, l => l.StartsWith("Follow Summon, if a rival chooses it", StringComparison.Ordinal));
        Assert.Contains(report.Lines, l => l == $"Seat 1 follows Summon: summons 2 Imps at {mine}.");
        Assert.Equal(new Army(2, 0), match.UnitsOn(mine));
        Assert.Equal(mana - 2 - 2 + Income(match, 0), match.Seats[0].Mana);
    }

    [Fact]
    public void A_follow_order_nobody_opens_does_nothing_and_costs_nothing()
    {
        var match = Opened(1);
        var mine = match.Seats[0].Tiles.First();
        var mana = match.Seats[0].Mana;

        var report = Play(match, Orders.Hold(0).Following(Orders.Summon(0, mine, UnitKind.Imp, 2)));

        Assert.Equal(Army.None, match.UnitsOn(mine));
        Assert.Equal(mana + Income(match, 0), match.Seats[0].Mana);
        Assert.Contains(match.ReportFor(report, 0).Lines, l => l.StartsWith("Nobody else chose Summon", StringComparison.Ordinal));
        Assert.DoesNotContain(match.ReportFor(report, 1).Lines, l => l.StartsWith("Nobody else chose", StringComparison.Ordinal)); // private
    }

    [Fact]
    public void The_one_who_chose_attune_wins_a_clash_with_a_follower()
    {
        var match = Enumerable.Range(1, 2000).Select(Opened).First(m => FreeNextTo(m, 0).Intersect(FreeNextTo(m, 1)).Any());
        var tile = FreeNextTo(match, 0).Intersect(FreeNextTo(match, 1)).First();
        var follower = match.Seats[1].Mana;

        var report = Play(match, Orders.Attune(0, tile), Orders.Hold(1).Following(Orders.Attune(1, tile)));

        Assert.Equal(0, match.OwnerOf(tile));
        Assert.Contains(report.Lines, l => l.Contains("but Seat 1 chose Attune and gets it", StringComparison.Ordinal));
        Assert.Equal(follower + Income(match, 1), match.Seats[1].Mana); // the follower pays nothing
    }

    public static TheoryData<string, string> BadFollows => new()
    {
        { "an attack", "an Attune, a Summon or a Build" },
        { "your own stance", "You already chose Summon" },
        { "beside reinforcements", "not both" },
        { "a summon on a rival's tile", "Follow order: Units can only be summoned on your own tiles" },
    };

    [Theory]
    [MemberData(nameof(BadFollows))]
    public void A_follow_order_that_doesnt_check_changes_nothing(string problem, string reason)
    {
        var match = Opened(1);
        var mine = match.Seats[0].Tiles.First();
        var theirs = match.Seats[1].Tiles.First();
        var orders = problem switch
        {
            "an attack" => Orders.Hold(0).Following(Orders.Attack(0, theirs, [new Move(mine, theirs, new Army(1, 0))])),
            "your own stance" => Orders.Summon(0, mine, UnitKind.Imp, 1).Following(Orders.Summon(0, mine, UnitKind.Imp, 1)),
            "beside reinforcements" => Orders.Hold(0).Following(Orders.Summon(0, mine, UnitKind.Imp, 1))
                with { Reinforce = new Reinforcement(mine, UnitKind.Imp, 1) },
            _ => Orders.Hold(0).Following(Orders.Summon(0, theirs, UnitKind.Imp, 1)),
        };
        var before = System.Text.Json.JsonSerializer.Serialize(match.Save());

        var check = match.Check(orders);
        var report = Play(match, orders);

        Assert.False(check.Succeeded);
        Assert.Contains(reason, check.Lines[0], StringComparison.Ordinal);
        Assert.False(report.Succeeded);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(match.Save()));
    }
}
