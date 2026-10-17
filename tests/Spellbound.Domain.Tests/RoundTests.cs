namespace Spellbound.Domain.Tests;

public class RoundTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default;

    [Fact]
    public void A_round_lasts_four_weeks()
    {
        var round = Round.First(T0, Rules.RoundLength);

        Assert.Equal(1, round.Number);
        Assert.True(round.IsOpen(T0));
        Assert.True(round.IsOpen(T0.AddDays(28).AddTicks(-1)));
        Assert.False(round.IsOpen(T0.AddDays(28)));
        Assert.True(round.HasEnded(T0.AddDays(28)));
        Assert.Equal(TimeSpan.FromDays(27), round.TimeLeft(T0.AddDays(1)));
        Assert.Equal(TimeSpan.Zero, round.TimeLeft(T0.AddDays(30)));
    }

    [Fact]
    public void Next_round_starts_when_the_reset_runs()
    {
        var resetAt = T0.AddDays(28).AddMinutes(1);

        var next = Round.First(T0, Rules.RoundLength).Next(resetAt, Rules.RoundLength);

        Assert.Equal(2, next.Number);
        Assert.Equal(resetAt, next.StartsAt);
        Assert.Equal(resetAt.AddDays(28), next.EndsAt);
    }

    [Fact]
    public void An_admin_can_move_the_end_but_not_before_the_start()
    {
        var round = Round.First(T0, Rules.RoundLength);

        Assert.Equal(T0.AddDays(35), round.WithEnd(T0.AddDays(35)).EndsAt);
        Assert.True(round.WithEnd(T0.AddDays(1)).HasEnded(T0.AddDays(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => round.WithEnd(T0));
        Assert.Equal(T0.AddDays(28), round.EndsAt); // records don't change in place
    }

    [Fact]
    public void Hall_of_fame_keeps_the_top_mages_by_land_and_networth()
    {
        var rules = Rules with { HallOfFameSize = 2, StartingGold = 200_000 };
        var small = Mage.Create("u1", "Ash", MagicSchool.Radiant, T0, rules);
        var big = Mage.Create("u2", "Birch", MagicSchool.Verdant, T0, rules);
        big.Explore(10, T0, rules);
        var armed = Mage.Create("u3", "Cedar", MagicSchool.Tidal, T0, rules);
        armed.Recruit("knights", 400, T0, rules); // more networth than Birch's extra land
        var round = Round.First(T0, rules.RoundLength);

        var fame = HallOfFame.Record(round, [small, big, armed], rules);

        Assert.Equal(1, fame.RoundNumber);
        Assert.Equal(3, fame.MageCount);
        Assert.Equal(["Birch", "Ash"], fame.ByLand.Select(e => e.Name)); // Ash beats Cedar on name at equal land
        Assert.Equal(["Cedar", "Birch"], fame.ByNetworth.Select(e => e.Name));
        Assert.Equal(1, fame.ByLand[0].Rank);
        Assert.Equal(big.Land, fame.ByLand[0].Land);
    }

    [Fact]
    public void Hall_of_fame_of_an_empty_round_is_empty()
    {
        var fame = HallOfFame.Record(Round.First(T0, Rules.RoundLength), [], Rules);

        Assert.Equal(0, fame.MageCount);
        Assert.Empty(fame.ByLand);
    }
}
