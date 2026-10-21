namespace Spellbound.Domain.Tests;

public class FavourTests
{
    private static Match Opened()
    {
        var random = new SeededRandom(1);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        return match;
    }

    private static Report Play(Match match, params Orders[] orders) =>
        match.Resolve([.. orders, .. match.Seats.Where(s => orders.All(o => o.Seat != s.Index)).Select(s => Orders.Hold(s.Index))],
            new SeededRandom(match.CurrentRound));

    [Fact]
    public void A_stance_nobody_chose_gets_cheaper_each_round_up_to_the_cap()
    {
        var match = Opened();
        var tile = match.Seats[0].Tiles.First();

        var report = Play(match);
        Assert.Contains(report.Lines, l => l == "Nobody chose Build this round: next round it costs 1 mana less.");
        Play(match);
        Play(match);
        Play(match);

        Assert.Equal(3, match.FavourOf(Stance.Build)); // capped
        var check = match.Check(Orders.Build(0, tile, Upgrade.Shrine));
        Assert.Contains(check.Lines, l => l.StartsWith("Build a Shrine", StringComparison.Ordinal) && l.Contains("for 2 mana", StringComparison.Ordinal));

        match = Match.Load(match.Save(), match.Rules); // favour is saved with the match
        var mana = match.Seats[0].Mana;
        var income = match.Seats[0].Tiles.Sum(h => match.Map[h].Yield!.Value) + 1; // the Shrine yields 1 more
        Play(match, Orders.Build(0, tile, Upgrade.Shrine));

        Assert.Equal(mana - 2 + income, match.Seats[0].Mana);
        Assert.Equal(0, match.FavourOf(Stance.Build)); // chosen, so it resets
    }

    [Fact]
    public void A_stance_somebody_chose_gets_no_favour()
    {
        var match = Opened();

        var report = Play(match, Orders.Summon(1, match.Seats[1].Tiles.First(), UnitKind.Imp, 1));

        Assert.Equal(0, match.FavourOf(Stance.Summon));
        Assert.Equal(1, match.FavourOf(Stance.Attune));
        Assert.DoesNotContain(report.Lines, l => l.StartsWith("Nobody chose Summon", StringComparison.Ordinal));
    }
}
