namespace Spellbound.Domain.Tests;

public class BotTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Bots_only_ever_give_bids_and_orders_that_check(int players)
    {
        // The simulation throws if a bot's bid or orders fail.
        var report = Simulation.Run(players, 25, players, GameRules.Default);

        Assert.Equal(25 * players, report.FinalScores.Count);
        Assert.True(report.WinsBySeat.Sum() >= 25);
    }

    [Fact]
    public void A_bot_that_cant_afford_the_smallest_bid_passes()
    {
        var match = Match.Start(3, GameRules.Default with { MinBid = 100 }, new SeededRandom(1));

        Assert.Equal(Bid.Pass(0), Bot.ChooseBid(match, 0, new SeededRandom(1)));
    }

    [Fact]
    public void A_bot_holds_when_nothing_it_would_do_checks()
    {
        var rules = GameRules.Default with
        {
            AttuneBasePrice = 1000, ImpCost = 1000, GolemCost = 1000,
            ScorchManaPerPower = 1000, WardCost = 1000, LeylineMinCost = 1000, BlightCost = 1000, LureCost = 1000,
        };
        var random = new SeededRandom(3);
        var match = Match.Start(3, rules, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        Assert.Equal(Orders.Hold(0).Target, Bot.ChooseOrders(match, 0, random).Target);
    }

    [Fact]
    public void The_same_arguments_give_the_same_report()
    {
        var report = Simulation.Run(3, 20, 5, GameRules.Default).ToMarkdown();

        Assert.Equal(report, Simulation.Run(3, 20, 5, GameRules.Default).ToMarkdown());
        Assert.NotEqual(report, Simulation.Run(3, 20, 6, GameRules.Default).ToMarkdown());
        Assert.Contains("## The opening", report, StringComparison.Ordinal);
        Assert.Contains("| 8 |", report, StringComparison.Ordinal);
        Assert.Contains("Wins by seat", report, StringComparison.Ordinal);
    }
}
