namespace Spellbound.Domain.Tests;

public class RoundTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Length = TimeSpan.FromDays(28);

    [Fact]
    public void A_round_lasts_four_weeks()
    {
        var round = Round.First(T0, Length);

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

        var next = Round.First(T0, Length).Next(resetAt, Length);

        Assert.Equal(2, next.Number);
        Assert.Equal(resetAt, next.StartsAt);
        Assert.Equal(resetAt.AddDays(28), next.EndsAt);
    }

    [Fact]
    public void An_admin_can_move_the_end_but_not_before_the_start()
    {
        var round = Round.First(T0, Length);

        Assert.Equal(T0.AddDays(35), round.WithEnd(T0.AddDays(35)).EndsAt);
        Assert.True(round.WithEnd(T0.AddDays(1)).HasEnded(T0.AddDays(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => round.WithEnd(T0));
        Assert.Equal(T0.AddDays(28), round.EndsAt); // records don't change in place
    }
}
