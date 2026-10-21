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

    [Theory]
    [InlineData(Plan.Balanced)]
    [InlineData(Plan.Expander)]
    [InlineData(Plan.Economist)]
    public void In_the_last_round_a_bot_with_nothing_to_attack_buys_an_upgrade(Plan plan)
    {
        // Mana left at the end scores nothing; an upgrade is a point.
        var random = new SeededRandom(3);
        var match = Match.Start(3, GameRules.Default with { Rounds = 1, UpgradeCost = 1 }, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        Assert.Equal(Stance.Build, Bot.ChooseOrders(match, 0, random, plan).Stance);
    }

    [Fact]
    public void In_the_last_round_a_bot_at_its_upgrade_cap_does_something_else()
    {
        var random = new SeededRandom(3);
        var match = Match.Start(3, GameRules.Default with { Rounds = 1, UpgradeCost = 1, UpgradeCap = 0 }, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Auction([.. match.Seats.Select(s => Bid.Pass(s.Index))], random);
        }

        var orders = Bot.ChooseOrders(match, 0, random);
        Assert.NotEqual(Stance.Build, orders.Stance);
        Assert.True(match.Check(orders).Succeeded);
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

    [Theory]
    [InlineData(Plan.Warlord)]
    [InlineData(Plan.Builder)]
    [InlineData(Plan.Spellcaster)]
    [InlineData(Plan.Expander)]
    [InlineData(Plan.Economist)]
    public void Every_plan_plays_a_whole_match_with_orders_that_check_and_replays_the_same(Plan plan)
    {
        static string PlayOut(Plan plan)
        {
            var random = new SeededRandom(5);
            var match = Match.Start(3, GameRules.Default, random);
            var lines = new List<string>();
            while (match.Phase != MatchPhase.Ended)
            {
                var report = match.Phase == MatchPhase.Opening
                    ? match.Auction([.. match.Seats.Select(s => Bot.ChooseBid(match, s.Index, random, plan))], random)
                    : match.Resolve([.. match.Seats.Select(s => Bot.ChooseOrders(match, s.Index, random, plan))], random);
                Assert.True(report.Succeeded, string.Join('\n', report.Lines));
                lines.AddRange(report.Lines);
            }

            return string.Join('\n', lines);
        }

        Assert.Equal(PlayOut(plan), PlayOut(plan));
    }

    [Fact]
    public void Plans_against_each_other_report_every_plan_and_replay_the_same()
    {
        var first = Simulation.RunPlans(5, 20, 3, GameRules.Default);
        var again = Simulation.RunPlans(5, 20, 3, GameRules.Default);

        Assert.Equal(first.ToMarkdown(), again.ToMarkdown());
        Assert.Equal(5, first.Plans.Count);
        Assert.All(first.Plans.Values, p => Assert.Equal(20, p.Seats)); // with 5 players, each specialist in every game
        Assert.Contains("## Plans", first.ToMarkdown(), StringComparison.Ordinal);
    }
}
