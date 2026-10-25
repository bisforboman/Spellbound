namespace Spellbound.Domain.Tests;

public class FogTests
{
    private static Match Opened(int seed)
    {
        var random = new SeededRandom(seed);
        var match = Match.Start(3, GameRules.Default, random);
        while (match.Phase == MatchPhase.Opening)
        {
            match.Draft(new Pick(match.Picker!.Value, match.ToPick[0]), random);
        }

        return match;
    }

    /// <summary>A tile of seat 2 that seat 1 can't see (and one it can), in the first seeded match that has both.</summary>
    private static (Match Match, Hex Far, Hex Near) Layout()
    {
        foreach (var seed in Enumerable.Range(1, 2000))
        {
            var match = Opened(seed);
            var far = match.Seats[1].Tiles.Where(h => !match.CanSee(0, h)).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
            var near = match.Seats[1].Tiles.Where(h => match.CanSee(0, h)).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
            if (far.Count > 0 && near.Count > 0)
            {
                return (match, far[0], near[0]);
            }
        }

        throw new InvalidOperationException("No layout found.");
    }

    private static Report Play(Match match, params Orders[] orders) =>
        match.Resolve([.. orders, .. match.Seats.Where(s => orders.All(o => o.Seat != s.Index)).Select(s => Scenarios.Hold(s.Index))],
            new SeededRandom(match.CurrentRound));

    [Fact]
    public void A_rivals_units_far_from_your_land_are_hidden_from_you_and_your_report()
    {
        var (match, far, _) = Layout();

        var report = Play(match, Orders.Summon(1, far, UnitKind.Imp, 3));
        var mine = match.ReportFor(report, 0);
        var theirs = match.ReportFor(report, 1);

        Assert.Null(match.UnitsSeenBy(0, far));
        Assert.Equal(new Army(3, 0), match.UnitsSeenBy(1, far));
        Assert.DoesNotContain(mine.Lines, l => l.Contains("summons", StringComparison.Ordinal));
        Assert.Contains(theirs.Lines, l => l.Contains($"Seat 2 summons 3 Imps at {far}", StringComparison.Ordinal));
        Assert.DoesNotContain(mine.Lines, l => l.StartsWith("Seat 2 collects", StringComparison.Ordinal)); // income is mana
        Assert.Contains(mine.Lines, l => l.StartsWith("Seat 2 banks", StringComparison.Ordinal)); // scores are public
        Assert.Null(match.ManaSeenBy(0, 1));
        Assert.Equal(match.Seats[0].Mana, match.ManaSeenBy(0, 0));
    }

    [Fact]
    public void Units_next_to_your_land_are_seen()
    {
        var (match, _, near) = Layout();

        var report = Play(match, Orders.Summon(1, near, UnitKind.Imp, 2));

        Assert.Equal(new Army(2, 0), match.UnitsSeenBy(0, near));
        Assert.Contains(match.ReportFor(report, 0).Lines, l => l.Contains($"Seat 2 summons 2 Imps at {near}", StringComparison.Ordinal));
    }

    [Fact]
    public void Everything_is_seen_once_the_match_is_over()
    {
        var (match, far, _) = Layout();
        Play(match, Orders.Summon(1, far, UnitKind.Imp, 3));
        Report report = null!;
        while (match.Phase != MatchPhase.Ended)
        {
            report = Play(match);
        }

        Assert.Equal(new Army(3, 0), match.UnitsSeenBy(0, far));
        Assert.Equal(match.Seats[1].Mana, match.ManaSeenBy(0, 1));
        Assert.Equal(report.Lines, match.ReportFor(report, 0).Lines);
    }

    [Fact]
    public void A_trait_choice_is_reported_only_to_the_seat_that_made_it()
    {
        var match = Match.Start(3, GameRules.Default, new SeededRandom(1));
        var report = match.ChooseTrait(1, match.Seats[1].Offer[0]);

        Assert.Empty(match.ReportFor(report, 0).Lines);
        Assert.Single(match.ReportFor(report, 1).Lines);
    }
}
