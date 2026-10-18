using System.Globalization;

namespace Spellbound.Domain;

/// <summary>
/// The Vigil (docs/game-design.md): a mage freezes its realm for a while. No turns come in, so nothing is earned, and it
/// can neither act nor be struck. It works by moving the turn clock on by the frozen time, so turns resume where they
/// left off, part of the way to the next one included. Only the turns stop: protection and the round's clock run on.
/// </summary>
public sealed partial class Mage
{
    public bool IsInVigil(DateTimeOffset now) => VigilEndsAt is { } ends && now < ends;

    public ActionReport StartVigil(TimeSpan duration, DateTimeOffset now, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        AccrueTurns(now, rules);
        if (VigilProblem(now) is { } vigil)
        {
            return ActionReport.Fail(vigil);
        }

        if (VigilsKept >= rules.VigilsPerRound)
        {
            return ActionReport.Fail(rules.VigilsPerRound == 1
                ? "You have already kept your Vigil this round."
                : $"You have already kept your {rules.VigilsPerRound} Vigils this round.");
        }

        if (duration <= TimeSpan.Zero || duration > rules.VigilMaxDuration)
        {
            return ActionReport.Fail($"A Vigil can last up to {Hours(rules.VigilMaxDuration)}.");
        }

        TurnsAccruedAt += duration;
        VigilEndsAt = now + duration;
        VigilsKept++;
        return new ActionReport(true, 0,
        [
            $"You keep a Vigil for {Hours(duration)}. Your realm is frozen: no turns come in, and you can neither act nor be struck.",
            "You can end it early; it still counts as this round's Vigil.",
        ]);
    }

    /// <summary>Ends the Vigil now, before its time; turns start coming in again from now.</summary>
    public ActionReport EndVigil(DateTimeOffset now, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (VigilEndsAt is not { } ends || now >= ends)
        {
            return ActionReport.Fail("You are not keeping a Vigil.");
        }

        TurnsAccruedAt -= ends - now;
        VigilEndsAt = now;
        AccrueTurns(now, rules);
        return new ActionReport(true, 0, ["You end your Vigil. Turns come in again from now."]);
    }

    /// <summary>Why the mage can't act because of its Vigil, or null.</summary>
    private string? VigilProblem(DateTimeOffset now) => IsInVigil(now)
        ? $"You are keeping a Vigil until {VigilEndsAt!.Value.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)} UTC. End it to act."
        : null;

    private static string Hours(TimeSpan span) =>
        span.TotalHours == 1 ? "1 hour" : $"{span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)} hours";
}
