namespace Spellbound.Domain.Tests;

// Exploring decays: from the Contest phase it yields less, and below the median it yields more (docs/game-design.md).
// A new mage holds 250 acres, so one turn finds 10 before these.
public class ExploreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default;

    private static Mage NewMage(string owner = "user-1") => Mage.Create(owner, "Merlin", MagicSchool.Dawn, T0, Rules);

    [Theory]
    [InlineData(SeasonPhase.Sanctuary, 0, 260)]
    [InlineData(SeasonPhase.Growth, 0, 260)]
    [InlineData(SeasonPhase.Contest, 0, 255)] // ×0.5
    [InlineData(SeasonPhase.Conjunction, 0, 255)]
    [InlineData(SeasonPhase.Growth, 300, 262)] // below the median: ×1.25, 12.5 rounded down
    [InlineData(SeasonPhase.Contest, 300, 256)] // both: 10 × 0.5 × 1.25 = 6.25
    [InlineData(SeasonPhase.Growth, 250, 260)] // at the median isn't below it
    public void One_turn_of_exploring_depends_on_the_phase_and_the_median(SeasonPhase phase, int median, int land)
    {
        var mage = NewMage();

        var report = mage.Explore(1, T0, Rules, new ExploreConditions(phase, median));

        Assert.True(report.Succeeded);
        Assert.Equal(land, mage.Land);
    }

    [Fact]
    public void The_bonus_stops_once_you_pass_the_median()
    {
        var mage = NewMage();

        mage.Explore(5, T0, Rules, new ExploreConditions(SeasonPhase.Growth, 270));

        // 12.5 and 11.9 below the median (262, then 274: past 270), then 9.1, 8.8, 8.6 without the bonus; with the
        // fractions carried over that is 12, 12, 9, 9, 8.
        Assert.Equal(250 + 12 + 12 + 9 + 9 + 8, mage.Land);
    }

    [Fact]
    public void A_big_realm_still_finds_land_in_the_contest()
    {
        var mage = Mage.Create("user-1", "Merlin", MagicSchool.Dawn, T0, Rules with { StartingLand = 2_500 });

        // 10 × 250 / 2,500 = 1 acre a turn, halved in the Contest: an acre every other turn, not one every turn.
        mage.Explore(10, T0, Rules, new ExploreConditions(SeasonPhase.Contest, 0));

        Assert.Equal(2_504, mage.Land); // the yield shrinks a little as the realm grows: 0.5, 0.4998, …
        Assert.True(mage.ExploreCarry is > 0.9 and < 1);
    }

    [Fact]
    public void The_numbers_come_from_the_games_rules()
    {
        var rules = Rules with { ExploreLateMultiplier = 0.3 };

        Assert.Equal(3, NewMage().ExploreYield(rules, new ExploreConditions(SeasonPhase.Contest, 0)), 6);
    }

    [Fact]
    public void A_failed_explore_changes_nothing()
    {
        var mage = NewMage();

        var report = mage.Explore(101, T0, Rules, new ExploreConditions(SeasonPhase.Contest, 300));

        Assert.False(report.Succeeded);
        Assert.Equal(250, mage.Land);
        Assert.Equal(100, mage.Turns);
        Assert.Equal(0, mage.ExploreCarry);
    }

    [Fact]
    public void The_conditions_read_the_round_and_the_mages()
    {
        var round = Round.First(T0, TimeSpan.FromDays(28));
        var small = NewMage("a");
        var big = NewMage("b");
        big.Explore(10, T0, Rules, new ExploreConditions(SeasonPhase.Growth, 0));

        var conditions = ExploreConditions.At(round, T0.AddDays(12), Rules, [small, big]);

        Assert.Equal(new ExploreConditions(SeasonPhase.Contest, (250 + big.Land) / 2), conditions);
    }

    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 300 }, 300)]
    [InlineData(new[] { 100, 300, 200 }, 200)]
    [InlineData(new[] { 100, 301, 200, 400 }, 250)] // (200 + 301) / 2, rounded down
    public void The_median_is_the_middle_mage(int[] lands, int median)
    {
        var mages = lands.Select((land, i) =>
        {
            var mage = Mage.Create($"user-{i}", $"Mage {i}", MagicSchool.Dawn, T0, Rules with { StartingLand = land });
            return mage;
        });

        Assert.Equal(median, Rankings.MedianLand(mages));
    }
}
