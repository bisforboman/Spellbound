namespace Spellbound.Domain.Tests;

// The phases only describe the round so far (later steps make rules read them), so there is no action here and no
// "changes nothing on failure" case.
public class SeasonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default;

    private static Round Days(int days) => Round.First(T0, TimeSpan.FromDays(days));

    [Theory]
    [InlineData(0, SeasonPhase.Sanctuary)]
    [InlineData(2.99, SeasonPhase.Sanctuary)] // the end of day 3
    [InlineData(3, SeasonPhase.Growth)] // the start of day 4
    [InlineData(9.99, SeasonPhase.Growth)]
    [InlineData(10, SeasonPhase.Contest)]
    [InlineData(17, SeasonPhase.Conjunction)]
    [InlineData(26.99, SeasonPhase.Conjunction)]
    [InlineData(27, SeasonPhase.Reckoning)]
    [InlineData(30, SeasonPhase.Reckoning)] // after the end, before the reset
    [InlineData(-1, SeasonPhase.Sanctuary)] // before the start
    public void A_28_day_round_runs_through_the_phases_by_day(double day, SeasonPhase phase)
    {
        Assert.Equal(phase, Season.PhaseAt(Days(28), T0.AddDays(day), Rules));
    }

    [Fact]
    public void A_7_day_round_has_the_same_shape()
    {
        var round = Days(7);

        Assert.Equal(SeasonPhase.Sanctuary, Season.PhaseAt(round, T0.AddHours(17.9), Rules));
        Assert.Equal(SeasonPhase.Growth, Season.PhaseAt(round, T0.AddHours(18), Rules)); // 3/28 of 7 days
        Assert.Equal(T0.AddHours(18), Season.NextPhaseAt(round, T0, Rules));
    }

    [Fact]
    public void The_next_phase_is_told_until_the_reckoning()
    {
        var round = Days(28);

        Assert.Equal(T0.AddDays(10), Season.NextPhaseAt(round, T0.AddDays(5), Rules));
        Assert.Null(Season.NextPhaseAt(round, T0.AddDays(27.5), Rules));
    }

    [Fact]
    public void The_boundaries_come_from_the_games_rules()
    {
        var rules = Rules with { SanctuaryEnds = 0.5 };

        Assert.Equal(SeasonPhase.Sanctuary, Season.PhaseAt(Days(28), T0.AddDays(13), rules));
    }
}
