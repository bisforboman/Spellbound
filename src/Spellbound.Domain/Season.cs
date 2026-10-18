namespace Spellbound.Domain;

/// <summary>The phases of a season (a round), in order (docs/mageborn-design.md, "Season").</summary>
public enum SeasonPhase
{
    /// <summary>The opening: nobody is struck unless they strike first.</summary>
    Sanctuary,

    /// <summary>Exploring is cheap; the first raids.</summary>
    Growth,

    /// <summary>Exploring slows sharply; land comes mainly from war.</summary>
    Contest,

    /// <summary>Armageddon can be cast; the Final War follows.</summary>
    Conjunction,

    /// <summary>Titles, the Hall of fame, then the reset.</summary>
    Reckoning,
}

/// <summary>
/// Which phase a round is in, from the share of it gone by and the boundaries in <see cref="GameRules"/>. Kept apart
/// from <see cref="Round"/>, which the game host uses and knows no game rules. Worked out from the round's current
/// start and end, so when an admin moves the end, the phases move with it.
/// </summary>
public static class Season
{
    public static SeasonPhase PhaseAt(Round round, DateTimeOffset now, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(rules);
        var share = Share(round, now);
        return share < rules.SanctuaryEnds ? SeasonPhase.Sanctuary
            : share < rules.GrowthEnds ? SeasonPhase.Growth
            : share < rules.ContestEnds ? SeasonPhase.Contest
            : share < rules.ConjunctionEnds ? SeasonPhase.Conjunction
            : SeasonPhase.Reckoning;
    }

    /// <summary>When the next phase begins, or null in the Reckoning (the last one).</summary>
    public static DateTimeOffset? NextPhaseAt(Round round, DateTimeOffset now, GameRules rules)
    {
        double? ends = PhaseAt(round, now, rules) switch
        {
            SeasonPhase.Sanctuary => rules.SanctuaryEnds,
            SeasonPhase.Growth => rules.GrowthEnds,
            SeasonPhase.Contest => rules.ContestEnds,
            SeasonPhase.Conjunction => rules.ConjunctionEnds,
            _ => null,
        };
        return ends is { } share ? round.StartsAt + ((round.EndsAt - round.StartsAt) * share) : null;
    }

    private static double Share(Round round, DateTimeOffset now) =>
        Math.Clamp((now - round.StartsAt) / (round.EndsAt - round.StartsAt), 0, 1);
}
