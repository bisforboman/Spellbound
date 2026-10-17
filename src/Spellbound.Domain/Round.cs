namespace Spellbound.Domain;

/// <summary>
/// One round: a number and a start and end. When it ends, everyone starts over in the next round. It knows nothing about
/// Spellbound's rules (the game passes the length, GameRules.RoundLength), so the reusable hosting code can run rounds.
/// </summary>
public sealed record Round(int Number, DateTimeOffset StartsAt, DateTimeOffset EndsAt)
{
    public static Round First(DateTimeOffset now, TimeSpan length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        return new Round(1, now, now + length);
    }

    /// <summary>The round after this one, starting now (the reset can run a little after the end).</summary>
    public Round Next(DateTimeOffset now, TimeSpan length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        return new Round(Number + 1, now, now + length);
    }

    /// <summary>
    /// The same round with a new end, for an admin extending or shortening it. An end at or before now ends the
    /// round at the next reset check. It can't be at or before the start.
    /// </summary>
    public Round WithEnd(DateTimeOffset endsAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endsAt, StartsAt);
        return this with { EndsAt = endsAt };
    }

    /// <summary>Whether players can act. Between the end and the reset, nothing can be done.</summary>
    public bool IsOpen(DateTimeOffset now) => now >= StartsAt && now < EndsAt;

    public bool HasEnded(DateTimeOffset now) => now >= EndsAt;

    public TimeSpan TimeLeft(DateTimeOffset now) => HasEnded(now) ? TimeSpan.Zero : EndsAt - now;
}
