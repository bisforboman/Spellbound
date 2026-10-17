namespace Spellbound.Domain.Tests;

public class TurnClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameRules Rules = GameRules.Default with { TurnInterval = TimeSpan.FromMinutes(6), MaxTurns = 200 };

    [Fact]
    public void Accrues_one_turn_per_interval()
    {
        var result = TurnClock.Accrue(10, T0, T0.AddMinutes(18), Rules);

        Assert.Equal(13, result.Turns);
        Assert.Equal(T0.AddMinutes(18), result.AccruedAt);
    }

    [Fact]
    public void Keeps_the_partial_interval()
    {
        var result = TurnClock.Accrue(10, T0, T0.AddMinutes(20), Rules);

        Assert.Equal(13, result.Turns);
        Assert.Equal(T0.AddMinutes(18), result.AccruedAt);
    }

    [Fact]
    public void Less_than_one_interval_changes_nothing()
    {
        var result = TurnClock.Accrue(10, T0, T0.AddMinutes(5), Rules);

        Assert.Equal(new TurnAccrual(10, T0), result);
    }

    [Fact]
    public void Stops_at_the_cap()
    {
        var now = T0.AddHours(1);

        var result = TurnClock.Accrue(195, T0, now, Rules);

        Assert.Equal(200, result.Turns);
        Assert.Equal(now, result.AccruedAt);
    }

    [Fact]
    public void Time_spent_at_the_cap_does_not_bank()
    {
        var syncedAtCap = TurnClock.Accrue(200, T0, T0.AddHours(5), Rules);

        // Spend some turns right away, then wait one interval: only one new turn.
        var afterOneInterval = TurnClock.Accrue(150, syncedAtCap.AccruedAt, syncedAtCap.AccruedAt.AddMinutes(6), Rules);

        Assert.Equal(151, afterOneInterval.Turns);
    }

    [Fact]
    public void Clock_going_backwards_is_ignored()
    {
        var result = TurnClock.Accrue(10, T0, T0.AddMinutes(-30), Rules);

        Assert.Equal(new TurnAccrual(10, T0), result);
    }

    [Fact]
    public void Time_until_next_turn_counts_down_from_the_last_accrual()
    {
        var remaining = TurnClock.TimeUntilNextTurn(10, T0, T0.AddMinutes(8), Rules);

        Assert.Equal(TimeSpan.FromMinutes(4), remaining);
    }

    [Fact]
    public void Time_until_next_turn_is_null_at_the_cap()
    {
        Assert.Null(TurnClock.TimeUntilNextTurn(200, T0, T0.AddMinutes(1), Rules));
    }
}
