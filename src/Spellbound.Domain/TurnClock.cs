namespace Spellbound.Domain;

/// <summary>Result of bringing a turn counter up to date.</summary>
/// <param name="Turns">Turns on hand after accrual.</param>
/// <param name="AccruedAt">Point in time the next interval should be measured from.</param>
public readonly record struct TurnAccrual(int Turns, DateTimeOffset AccruedAt);

/// <summary>
/// Lazy turn regeneration: store (turns, accruedAt) and derive the current value on read.
/// No timers or background jobs are needed.
/// </summary>
public static class TurnClock
{
    public static TurnAccrual Accrue(int currentTurns, DateTimeOffset accruedAt, DateTimeOffset now, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.TurnInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("TurnInterval must be positive.", nameof(rules));
        }

        // At (or above) the cap, time does not bank: restart the interval from now.
        if (currentTurns >= rules.MaxTurns)
        {
            return new TurnAccrual(currentTurns, now > accruedAt ? now : accruedAt);
        }

        // Clock went backwards or no time passed: nothing to do.
        if (now <= accruedAt)
        {
            return new TurnAccrual(currentTurns, accruedAt);
        }

        var intervalTicks = rules.TurnInterval.Ticks;
        var intervals = (now - accruedAt).Ticks / intervalTicks;
        if (intervals == 0)
        {
            return new TurnAccrual(currentTurns, accruedAt);
        }

        var total = currentTurns + intervals;
        if (total >= rules.MaxTurns)
        {
            return new TurnAccrual(rules.MaxTurns, now);
        }

        // Keep the leftover partial interval by advancing accruedAt only by whole intervals.
        return new TurnAccrual((int)total, accruedAt + TimeSpan.FromTicks(intervals * intervalTicks));
    }

    /// <summary>Time until the next turn arrives, or null when the turn cap is reached.</summary>
    public static TimeSpan? TimeUntilNextTurn(int currentTurns, DateTimeOffset accruedAt, DateTimeOffset now, GameRules rules)
    {
        var accrual = Accrue(currentTurns, accruedAt, now, rules);
        if (accrual.Turns >= rules.MaxTurns)
        {
            return null;
        }

        var remaining = accrual.AccruedAt + rules.TurnInterval - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
